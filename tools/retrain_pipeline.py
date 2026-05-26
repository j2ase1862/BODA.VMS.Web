#!/usr/bin/env python3
"""
Predictive_DefectRate_Plan Phase F — 주간 재학습 통합 래퍼.

4단계를 한 번에 실행:
    1. export_training_data.py   (v_defect_features → Parquet)
    2. train_baseline.py         (LightGBM 학습 + 평가)
    3. export_to_onnx.py         (.txt → .onnx + 검증)
    4. register_model.py --activate (MLModels INSERT + 핫스왑)

Windows Task Scheduler 주간 실행 대상:
    schtasks /Create /SC WEEKLY /D SUN /TN "VMS-Retrain" /TR ^
        "python D:\\Project\\BODA.VMS.Web\\tools\\retrain_pipeline.py ^
            --db ""C:\\ProgramData\\BODA\\VMS\\BodaVision.db"" ^
            --workdir D:\\Project\\BODA.VMS.Web\\models"

Linux cron:
    0 3 * * 0  python tools/retrain_pipeline.py --db /var/lib/boda/BodaVision.db --workdir /var/lib/boda/models

Usage:
    python tools/retrain_pipeline.py \\
        --db "C:/ProgramData/BODA/VMS/BodaVision.db" \\
        --workdir models/ \\
        [--model-name defect_rate_60m] \\
        [--horizon-hours 1] \\
        [--skip-activate]   # IsActive=0 으로만 등록 (수동 검증 후 활성화)
        [--gate-r2 0.3]     # baseline R² 이 미만이면 등록 단계 skip

Behavior:
    • 각 단계 실패 시 즉시 종료 (cron 로그에 stderr 보존).
    • Version 태그 = "YYYY.MM.DD-HHMM" UTC 자동 부여.
    • Baseline 게이트(R² > --gate-r2) 미통과 시 ONNX 등록 skip — 운영 모델 회귀 방지.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

THIS_DIR = Path(__file__).resolve().parent


def run_step(name: str, cmd: list[str]) -> None:
    """단계 실행 — 실패 시 즉시 종료."""
    print(f"\n[retrain] ============================================")
    print(f"[retrain]  STEP {name}")
    print(f"[retrain]  $ {' '.join(cmd)}")
    print(f"[retrain] ============================================")
    result = subprocess.run(cmd)
    if result.returncode != 0:
        sys.stderr.write(f"[retrain] FAILED — step '{name}' exit {result.returncode}\n")
        sys.exit(result.returncode)


def main() -> int:
    ap = argparse.ArgumentParser(
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--db", required=True, help="BodaVision.db path")
    ap.add_argument("--workdir", required=True, help="Output directory for parquet/models")
    ap.add_argument("--model-name", default="defect_rate_60m",
                    help="MLModel.Name (default: defect_rate_60m)")
    ap.add_argument("--horizon-hours", type=int, default=1,
                    help="Hours-ahead label horizon (default: 1)")
    ap.add_argument("--skip-activate", action="store_true",
                    help="Register row with IsActive=0 — operator must flip manually")
    ap.add_argument("--gate-r2", type=float, default=0.3,
                    help="Skip registration if baseline R² < this (default: 0.3 — Plan §6 Phase D gate)")
    args = ap.parse_args()

    db_path = Path(args.db)
    if not db_path.exists():
        sys.stderr.write(f"DB not found: {db_path}\n")
        return 1

    workdir = Path(args.workdir)
    workdir.mkdir(parents=True, exist_ok=True)

    # Version tag — UTC 분 단위 (동일 날짜 다중 재학습 시 충돌 회피)
    version = datetime.now(timezone.utc).strftime("%Y.%m.%d-%H%M")

    parquet_path = workdir / f"training_{version}.parquet"
    model_txt = workdir / f"{args.model_name}_{version}.txt"
    metrics_path = workdir / f"{args.model_name}_{version}.metrics.json"
    spec_path = workdir / f"{args.model_name}_{version}.featurespec.json"
    onnx_path = workdir / f"{args.model_name}_{version}.onnx"

    python_exe = sys.executable

    # 1. Export
    run_step("1/4 export_training_data", [
        python_exe, str(THIS_DIR / "export_training_data.py"),
        "--db", str(db_path),
        "--out", str(parquet_path),
        "--horizon-hours", str(args.horizon_hours),
    ])

    if not parquet_path.exists() or parquet_path.stat().st_size == 0:
        sys.stderr.write("[retrain] parquet empty/missing — skipping training\n")
        return 1

    # 2. Train baseline
    # 모델 파일명 일관성을 위해 --model-name 에 version stem 만 전달
    model_stem = f"{args.model_name}_{version}"
    run_step("2/4 train_baseline", [
        python_exe, str(THIS_DIR / "train_baseline.py"),
        "--parquet", str(parquet_path),
        "--out-dir", str(workdir),
        "--model-name", model_stem,
    ])

    if not model_txt.exists() or not metrics_path.exists():
        sys.stderr.write("[retrain] trainer outputs missing — abort\n")
        return 1

    # Gate: R² 미통과 시 등록 skip — 운영 모델 회귀 방지
    metrics = json.loads(metrics_path.read_text(encoding="utf-8"))
    r2 = float(metrics.get("r2", 0.0))
    if r2 < args.gate_r2:
        sys.stderr.write(
            f"[retrain] GATE FAILED — R²={r2:.4f} < --gate-r2={args.gate_r2}. "
            "Skipping ONNX export + registration. Existing active model unchanged.\n"
        )
        # 학습 산출물은 보존 (운영자가 수동 점검 가능). exit 0 — cron 알람 노이즈 회피.
        return 0

    # 3. ONNX export
    run_step("3/4 export_to_onnx", [
        python_exe, str(THIS_DIR / "export_to_onnx.py"),
        "--model", str(model_txt),
        "--out", str(onnx_path),
    ])

    if not onnx_path.exists():
        sys.stderr.write("[retrain] onnx export missing — abort\n")
        return 1

    # 4. Register
    register_cmd = [
        python_exe, str(THIS_DIR / "register_model.py"),
        "--db", str(db_path),
        "--name", args.model_name,
        "--version", version,
        "--onnx", str(onnx_path),
        "--metrics", str(metrics_path),
        "--spec", str(spec_path),
    ]
    if not args.skip_activate:
        register_cmd.append("--activate")
    run_step("4/4 register_model", register_cmd)

    print()
    print(f"[retrain] SUCCESS — model '{args.model_name}' v{version} registered "
          f"({'active' if not args.skip_activate else 'inactive'}).")
    print(f"[retrain] PredictionService 가 다음 캐시 만료(≤60s) 시점에 자동 핫스왑.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
