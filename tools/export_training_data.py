#!/usr/bin/env python3
"""
Predictive_DefectRate_Plan Phase B — Training data exporter.

SQLite (read-only) → Parquet, with target alignment and rolling lag features.

The Web app (Program.cs) creates the v_defect_features VIEW. This script is the
SINGLE consumer of that VIEW for training data — keeping definition unified
between training, debug and (later) inference (plan §6 Phase B).

Usage:
    python tools/export_training_data.py \\
        --db "C:/ProgramData/BODA/VMS/BodaVision.db" \\
        --out data/training_v1.parquet \\
        [--horizon-hours 1]

Read-only safety:
    Opens SQLite via URI mode=ro — does NOT acquire write lock and is safe to
    run against the live production DB. WAL mode (set by the Web app) means
    even active writes are non-blocking.

Output schema (per row, one per (ClientId, RecipeName, HourBucket)):
    Keys       : ClientId, RecipeName, HourBucket (UTC, hour-aligned)
    Target     : TargetNgRate — NgRate of (HourBucket + horizon_hours)
    Current-h  : InspectionCount, NgCount, NgRate,
                 AvgBrightness, AvgContrastStd, AvgFocusScore,
                 AvgBlobCount, AvgMaxBlobAreaPx,
                 AvgCycleTimeMs, AvgDlConfidence, MinDlConfidence,
                 NullRate{Brightness,ContrastStd,FocusScore,CycleTimeMs,DlConfidence},
                 ShiftId, DistinctShiftCount, DistinctOperatorCount,
                 DistinctLotCount, DistinctWorkOrderCount
    Lagged     : Lag{1,4,24}h_<col> for selected cols (NgRate, AvgFocusScore,
                 AvgDlConfidence, MinDlConfidence, AvgCycleTimeMs, AvgBrightness)

Row filtering:
    Rows with NULL TargetNgRate (no future window observed yet) are dropped.
"""

from __future__ import annotations

import argparse
import os
import sqlite3
import sys
from pathlib import Path
from urllib.parse import quote

import pandas as pd


# 시간 윈도우 lag — plan §2.2 (A) 카테고리: 직전 1h/4h/24h
LAG_HOURS: list[int] = [1, 4, 24]

# Lag 를 만들 컬럼 (모두에 만들면 column 폭발 — 의미 있는 핵심만)
LAG_COLS: list[str] = [
    "NgRate",
    "AvgFocusScore",
    "AvgDlConfidence",
    "MinDlConfidence",
    "AvgCycleTimeMs",
    "AvgBrightness",
]

GROUP_KEYS: list[str] = ["ClientId", "RecipeName"]


def read_only_uri(db_path: str) -> str:
    """Build SQLite URI for read-only access — safe against live DB."""
    abs_path = os.path.abspath(db_path).replace("\\", "/")
    # quote 해야 경로 내 공백/한글이 안전. URI 스킴은 file:
    return f"file:/{quote(abs_path)}?mode=ro"


def load_hourly(db_path: str) -> pd.DataFrame:
    """Read v_defect_features VIEW into a DataFrame with parsed HourBucket."""
    uri = read_only_uri(db_path)
    conn = sqlite3.connect(uri, uri=True)
    try:
        df = pd.read_sql_query(
            "SELECT * FROM v_defect_features "
            "ORDER BY ClientId, RecipeName, HourBucket",
            conn,
            parse_dates=["HourBucket"],
        )
    finally:
        conn.close()
    return df


def add_target(df: pd.DataFrame, horizon_hours: int) -> pd.DataFrame:
    """Append TargetNgRate = NgRate shifted -horizon hours per (Client, Recipe).

    Caller must have sorted by group keys + HourBucket. shift(-n) on a sorted
    sequence aligns row t with the NgRate observed at row t+n.

    Limitation: shift counts ROWS, not clock hours. If an hour bucket has zero
    inspections it's absent — so shift(-1) may skip across a gap. For baseline
    use this is acceptable; phase D can reindex to a complete hourly grid.
    """
    df["TargetNgRate"] = (
        df.groupby(GROUP_KEYS)["NgRate"].shift(-horizon_hours)
    )
    return df


def add_lags(df: pd.DataFrame) -> pd.DataFrame:
    """Append Lag{h}h_<col> for each (col, h) in LAG_COLS × LAG_HOURS."""
    for lag in LAG_HOURS:
        for col in LAG_COLS:
            df[f"Lag{lag}h_{col}"] = df.groupby(GROUP_KEYS)[col].shift(lag)
    return df


def write_parquet(df: pd.DataFrame, out_path: str) -> None:
    out = Path(out_path)
    out.parent.mkdir(parents=True, exist_ok=True)
    df.to_parquet(out, index=False)


def summarize(df: pd.DataFrame) -> None:
    print("\n[export] === summary ===")
    print(f"  rows         : {len(df):,}")
    print(f"  columns      : {len(df.columns)}")
    if "TargetNgRate" in df.columns and len(df) > 0:
        tgt = df["TargetNgRate"]
        print(f"  target mean  : {tgt.mean():.4f}")
        print(f"  target std   : {tgt.std():.4f}")
        print(f"  target min   : {tgt.min():.4f}")
        print(f"  target max   : {tgt.max():.4f}")
    if "ClientId" in df.columns:
        print(f"  clients      : {df['ClientId'].nunique()}")
    if "RecipeName" in df.columns:
        print(f"  recipes      : {df['RecipeName'].nunique()}")


def main() -> int:
    parser = argparse.ArgumentParser(
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    parser.add_argument(
        "--db",
        required=True,
        help="Path to BodaVision.db (e.g. C:/ProgramData/BODA/VMS/BodaVision.db)",
    )
    parser.add_argument(
        "--out",
        required=True,
        help="Output Parquet path (parents auto-created)",
    )
    parser.add_argument(
        "--horizon-hours",
        type=int,
        default=1,
        help="Hours ahead to predict (default: 1 — plan §2.1)",
    )
    args = parser.parse_args()

    if not os.path.exists(args.db):
        sys.stderr.write(f"[export] DB not found: {args.db}\n")
        return 1
    if args.horizon_hours < 1:
        sys.stderr.write("[export] --horizon-hours must be >= 1\n")
        return 1

    print(f"[export] reading {args.db} (read-only)")
    df = load_hourly(args.db)
    print(f"[export] hourly buckets: {len(df):,}")

    if len(df) == 0:
        # 데이터가 아직 없을 때도 동일 스키마의 빈 parquet 을 만들어 후속 단계 안전
        write_parquet(df, args.out)
        print(f"[export] empty VIEW — wrote empty parquet → {args.out}")
        return 0

    df = add_target(df, args.horizon_hours)
    df = add_lags(df)

    before = len(df)
    df = df.dropna(subset=["TargetNgRate"]).reset_index(drop=True)
    dropped = before - len(df)
    print(f"[export] dropped {dropped:,} rows with NULL target")

    write_parquet(df, args.out)
    print(f"[export] wrote {len(df):,} rows × {len(df.columns)} cols → {args.out}")

    summarize(df)
    return 0


if __name__ == "__main__":
    sys.exit(main())
