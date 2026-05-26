#!/usr/bin/env python3
"""
Predictive_DefectRate_Plan Phase C — LightGBM baseline trainer.

Usage:
    python tools/train_baseline.py \\
        --parquet data/training_v1.parquet \\
        --out-dir models/ \\
        [--model-name baseline]

Inputs:
    Parquet from tools/export_training_data.py (must include TargetNgRate column).

Outputs (in --out-dir):
    {name}.txt              — LightGBM native model (saved at best_iteration)
    {name}.metrics.json     — MAE / R² / pinball@0.9 / row counts / lift
    {name}.featurespec.json — feature order, categorical mask, split cutoff,
                              top-10 permutation importance.
                              (drop into MLModel.FeatureSpecJson at registration)

Train/val split:
    Global time-based — bottom 80% of HourBucket → train, top 20% → val.
    Why global (not per-pair): production inference predicts the *future* given
    the past. Per-pair split would let the model see future rows during training
    via shared time ranges across pairs.

Gate decision (plan §6 Phase D entry condition):
    R² > 0.30 AND ≥ 6 features with permutation_importance > 0.05
    → console banner highlights pass / fail.

Determinism: RANDOM_STATE = 42 fixed.
"""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import asdict, dataclass
from pathlib import Path

import numpy as np
import pandas as pd

try:
    import lightgbm as lgb
    from lightgbm import LGBMRegressor
except ImportError:
    sys.stderr.write("missing dep: pip install lightgbm\n")
    sys.exit(1)

try:
    from sklearn.inspection import permutation_importance
    from sklearn.metrics import mean_absolute_error, r2_score
except ImportError:
    sys.stderr.write("missing dep: pip install scikit-learn\n")
    sys.exit(1)


# Phase B exporter 가 만드는 키 컬럼 — 학습 피처에서 제외
KEY_COLS: set[str] = {"ClientId", "RecipeName", "HourBucket"}
TARGET_COL: str = "TargetNgRate"
# 작은 정수 코드 — LightGBM 에 categorical 로 명시해야 ordinality 가정 안 함
CATEGORICAL_COLS: list[str] = ["ShiftId"]

VAL_FRACTION: float = 0.2
RANDOM_STATE: int = 42

# Phase D 진입 게이트 (plan §6)
GATE_R2: float = 0.30
GATE_FEATURE_COUNT: int = 6
GATE_FEATURE_IMPORTANCE: float = 0.05


@dataclass
class BaselineMetrics:
    mae: float
    r2: float
    pinball_90: float
    train_rows: int
    val_rows: int
    train_target_mean: float
    val_target_mean: float
    constant_baseline_mae: float
    lift_over_constant: float
    best_iteration: int


def pinball_loss(y_true: np.ndarray, y_pred: np.ndarray, alpha: float) -> float:
    """Quantile (pinball) loss. alpha=0.9 emphasizes under-prediction penalty —
    아래로 빗나가면 over-confident OK 알림이 NG 를 놓치므로 비대칭 페널티가 운영상 더 유의."""
    diff = y_true - y_pred
    return float(np.mean(np.maximum(alpha * diff, (alpha - 1.0) * diff)))


def load_data(parquet: Path) -> pd.DataFrame:
    df = pd.read_parquet(parquet)
    if TARGET_COL not in df.columns:
        sys.stderr.write(f"target column '{TARGET_COL}' missing — re-run exporter\n")
        sys.exit(1)
    df = df.dropna(subset=[TARGET_COL]).reset_index(drop=True)
    df["HourBucket"] = pd.to_datetime(df["HourBucket"])
    return df


def time_split(df: pd.DataFrame) -> tuple[pd.DataFrame, pd.DataFrame]:
    df = df.sort_values("HourBucket").reset_index(drop=True)
    cutoff = int(len(df) * (1.0 - VAL_FRACTION))
    return df.iloc[:cutoff].copy(), df.iloc[cutoff:].copy()


def prepare_features(df: pd.DataFrame) -> tuple[list[str], list[str]]:
    feature_cols = [c for c in df.columns if c not in KEY_COLS and c != TARGET_COL]
    cat_features = [c for c in feature_cols if c in CATEGORICAL_COLS]
    return feature_cols, cat_features


def train_model(
    train_df: pd.DataFrame,
    val_df: pd.DataFrame,
    features: list[str],
    cats: list[str],
) -> LGBMRegressor:
    model = LGBMRegressor(
        objective="regression_l1",  # MAE — outlier NG burst 에 robust
        learning_rate=0.05,
        num_leaves=31,
        n_estimators=500,
        min_child_samples=20,
        colsample_bytree=0.9,
        subsample=0.9,
        subsample_freq=5,
        random_state=RANDOM_STATE,
        verbose=-1,
    )
    model.fit(
        train_df[features],
        train_df[TARGET_COL],
        eval_set=[(val_df[features], val_df[TARGET_COL])],
        eval_metric="mae",
        categorical_feature=cats if cats else "auto",
        callbacks=[lgb.early_stopping(stopping_rounds=30, verbose=False)],
    )
    return model


def evaluate(
    model: LGBMRegressor,
    train_df: pd.DataFrame,
    val_df: pd.DataFrame,
    features: list[str],
) -> BaselineMetrics:
    y_pred = model.predict(val_df[features])
    y_true = val_df[TARGET_COL].to_numpy()

    train_mean = float(train_df[TARGET_COL].mean())
    constant_pred = np.full_like(y_true, train_mean)
    constant_mae = float(mean_absolute_error(y_true, constant_pred))
    mae = float(mean_absolute_error(y_true, y_pred))

    return BaselineMetrics(
        mae=mae,
        r2=float(r2_score(y_true, y_pred)),
        pinball_90=pinball_loss(y_true, y_pred, alpha=0.9),
        train_rows=int(len(train_df)),
        val_rows=int(len(val_df)),
        train_target_mean=train_mean,
        val_target_mean=float(y_true.mean()),
        constant_baseline_mae=constant_mae,
        lift_over_constant=constant_mae - mae,
        best_iteration=int(model.best_iteration_ or model.n_estimators),
    )


def permutation_imp(
    model: LGBMRegressor,
    val_df: pd.DataFrame,
    features: list[str],
) -> pd.DataFrame:
    # sklearn permutation_importance — LGBMRegressor 가 sklearn estimator 인터페이스 이므로 wrapper 불필요
    result = permutation_importance(
        model,
        val_df[features],
        val_df[TARGET_COL],
        n_repeats=5,
        random_state=RANDOM_STATE,
        scoring="neg_mean_absolute_error",
        n_jobs=1,  # LightGBM 자체가 멀티스레드 → joblib 중첩 회피
    )
    return (
        pd.DataFrame(
            {
                "feature": features,
                "importance_mean": result.importances_mean,
                "importance_std": result.importances_std,
            }
        )
        .sort_values("importance_mean", ascending=False)
        .reset_index(drop=True)
    )


def gate(
    metrics: BaselineMetrics, importance: pd.DataFrame
) -> tuple[bool, bool, bool, int]:
    r2_pass = metrics.r2 > GATE_R2
    strong = int((importance["importance_mean"] > GATE_FEATURE_IMPORTANCE).sum())
    feat_pass = strong >= GATE_FEATURE_COUNT
    return (r2_pass and feat_pass), r2_pass, feat_pass, strong


def main() -> int:
    ap = argparse.ArgumentParser(
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--parquet", required=True, help="Parquet from exporter")
    ap.add_argument("--out-dir", required=True, help="Output directory for artifacts")
    ap.add_argument(
        "--model-name", default="baseline", help="Filename stem (default: baseline)"
    )
    args = ap.parse_args()

    parquet = Path(args.parquet)
    if not parquet.exists():
        sys.stderr.write(f"parquet not found: {parquet}\n")
        return 1

    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    print(f"[train] loading {parquet}")
    df = load_data(parquet)
    print(f"[train] rows after dropna(target): {len(df):,}")

    if len(df) < 100:
        sys.stderr.write(
            f"WARNING: only {len(df)} rows. Plan §3.3 requires ≥3 months. "
            "Baseline will be unreliable.\n"
        )
    if df[TARGET_COL].nunique() < 2:
        sys.stderr.write(
            "WARNING: target has < 2 unique values — model will be degenerate.\n"
        )

    train_df, val_df = time_split(df)
    print(f"[train] split → train={len(train_df):,}  val={len(val_df):,}")
    if len(train_df) == 0 or len(val_df) == 0:
        sys.stderr.write("ERROR: empty train or val split — not enough data\n")
        return 1
    print(f"[train] split point HourBucket={train_df['HourBucket'].max()}")

    features, cats = prepare_features(train_df)
    print(f"[train] features: {len(features)} ({len(cats)} categorical)")

    model = train_model(train_df, val_df, features, cats)
    metrics = evaluate(model, train_df, val_df, features)
    importance = permutation_imp(model, val_df, features)

    model_path = out_dir / f"{args.model_name}.txt"
    metrics_path = out_dir / f"{args.model_name}.metrics.json"
    spec_path = out_dir / f"{args.model_name}.featurespec.json"

    # best_iteration 까지만 저장 → 추론 시점에 추가 trees 무시 없이 정확
    model.booster_.save_model(str(model_path), num_iteration=metrics.best_iteration)
    metrics_path.write_text(
        json.dumps(asdict(metrics), indent=2, ensure_ascii=False), encoding="utf-8"
    )

    spec = {
        "feature_order": features,
        "categorical_features": cats,
        "target": TARGET_COL,
        "train_split_cutoff_utc": train_df["HourBucket"].max().isoformat(),
        "permutation_importance_top10": importance.head(10).to_dict(orient="records"),
        "random_state": RANDOM_STATE,
        "val_fraction": VAL_FRACTION,
    }
    spec_path.write_text(
        json.dumps(spec, indent=2, ensure_ascii=False, default=float),
        encoding="utf-8",
    )

    print()
    print("[train] === metrics ===")
    print(f"  MAE              : {metrics.mae:.5f}")
    print(f"  R²               : {metrics.r2:.4f}")
    print(f"  pinball@0.9      : {metrics.pinball_90:.5f}")
    print(f"  val target mean  : {metrics.val_target_mean:.5f}")
    print(f"  constant-pred MAE: {metrics.constant_baseline_mae:.5f}")
    direction = "BETTER" if metrics.lift_over_constant > 0 else "WORSE"
    print(
        f"  lift over const  : {metrics.lift_over_constant:+.5f}  ({direction} than naive)"
    )
    print(f"  best iteration   : {metrics.best_iteration}")

    print()
    print("[train] === permutation importance (top 10) ===")
    print(importance.head(10).to_string(index=False, float_format="%.5f"))

    print()
    print("[train] === gate decision (plan §6 Phase D entry) ===")
    passed, r2_pass, feat_pass, strong = gate(metrics, importance)
    print(
        f"  R² > {GATE_R2:.2f}                     : "
        f"{'PASS' if r2_pass else 'FAIL'}  ({metrics.r2:.3f})"
    )
    print(
        f"  features w/ importance > {GATE_FEATURE_IMPORTANCE:.2f} : "
        f"{'PASS' if feat_pass else 'FAIL'}  "
        f"({strong} ≥ {GATE_FEATURE_COUNT} required)"
    )
    print()
    print(
        f"  → {'GO Phase D (DL)' if passed else 'STAY at baseline — improve data/features first'}"
    )

    print()
    print("[train] artifacts:")
    print(f"  model        : {model_path}")
    print(f"  metrics      : {metrics_path}")
    print(f"  feature spec : {spec_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
