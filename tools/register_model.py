#!/usr/bin/env python3
"""
Predictive_DefectRate_Plan Phase D/E — MLModels DB 등록 헬퍼.

ONNX + metrics + featurespec → BodaVision.db MLModels 테이블 row INSERT.
--activate 시 같은 Name 의 기존 row 들을 IsActive=0 으로 토글한 뒤 신규를 IsActive=1.

PredictionService 는 60s 캐시 만료 후 다음 요청에서 active 모델 변경을 자동 감지 →
별도 reload 없이 핫스왑.

Usage:
    python tools/register_model.py \\
        --db        "C:/ProgramData/BODA/VMS/BodaVision.db" \\
        --name      defect_rate_60m \\
        --version   2026.05.26-r1 \\
        --onnx      models/baseline.onnx \\
        --metrics   models/baseline.metrics.json \\
        --spec      models/baseline.featurespec.json \\
        [--activate] \\
        [--dry-run]

Notes:
    • --dry-run 으로 SQL 만 출력하고 실제 쓰기 안 함 (운영 DB 확인용).
    • OnnxPath 는 절대 경로로 저장(서버 .NET 의 File.Exists 가 통과해야 함).
    • Mae / R2 는 metrics.json 에서 직접 읽음 — 누락 시 0 으로 폴백.
"""

from __future__ import annotations

import argparse
import json
import os
import sqlite3
import sys
from datetime import datetime, timezone
from pathlib import Path


def load_json(path: Path) -> dict:
    if not path.exists():
        sys.stderr.write(f"file not found: {path}\n")
        sys.exit(1)
    return json.loads(path.read_text(encoding="utf-8"))


def ensure_active_table_exists(conn: sqlite3.Connection) -> None:
    """MLModels 테이블이 없으면 (Web 앱이 부트스트랩 안 했으면) 사용자에게 알림.

    Web 부트스트랩 (Program.cs §2-j) 가 같은 스키마로 만들기 때문에 일치 보장.
    """
    cur = conn.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name='MLModels'"
    )
    if cur.fetchone() is None:
        sys.stderr.write(
            "MLModels table not found. Start the Web app at least once "
            "to bootstrap schema (Program.cs §2-j).\n"
        )
        sys.exit(1)


def main() -> int:
    ap = argparse.ArgumentParser(
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--db", required=True)
    ap.add_argument("--name", required=True, help="MLModel.Name (e.g. defect_rate_60m)")
    ap.add_argument("--version", required=True, help="MLModel.Version (e.g. 2026.05.26-r1)")
    ap.add_argument("--onnx", required=True, help="Path to .onnx file (absolute saved)")
    ap.add_argument("--metrics", required=True, help="metrics.json from trainer")
    ap.add_argument("--spec", required=True, help="featurespec.json from trainer")
    ap.add_argument(
        "--activate",
        action="store_true",
        help="Set new row IsActive=1 and deactivate other rows with same Name",
    )
    ap.add_argument(
        "--dry-run",
        action="store_true",
        help="Print SQL without writing",
    )
    args = ap.parse_args()

    db_path = Path(args.db)
    if not db_path.exists():
        sys.stderr.write(f"DB not found: {db_path}\n")
        return 1

    onnx_path = Path(args.onnx)
    if not onnx_path.exists():
        sys.stderr.write(f"ONNX not found: {onnx_path}\n")
        return 1
    onnx_abs = str(onnx_path.resolve())

    metrics = load_json(Path(args.metrics))
    spec = load_json(Path(args.spec))

    mae = float(metrics.get("mae", 0.0))
    r2 = float(metrics.get("r2", 0.0))
    trained_at = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S")
    feature_spec_json = json.dumps(spec, ensure_ascii=False)
    is_active = 1 if args.activate else 0

    print("[register] would insert MLModels row:")
    print(f"  Name             : {args.name}")
    print(f"  Version          : {args.version}")
    print(f"  OnnxPath         : {onnx_abs}")
    print(f"  Mae              : {mae:.6f}")
    print(f"  R2               : {r2:.6f}")
    print(f"  TrainedAt        : {trained_at}")
    print(f"  IsActive         : {is_active}")
    print(f"  FeatureSpecJson  : {len(feature_spec_json):,} bytes")

    if args.dry_run:
        print("[register] --dry-run — no DB writes")
        return 0

    # WAL mode 라 운영 Web 과 동시 쓰기 안전.
    conn = sqlite3.connect(str(db_path))
    try:
        ensure_active_table_exists(conn)

        if args.activate:
            cur = conn.execute(
                "UPDATE MLModels SET IsActive=0 WHERE Name=? AND IsActive=1",
                (args.name,),
            )
            print(f"[register] deactivated {cur.rowcount} prior active row(s) for Name='{args.name}'")

        try:
            conn.execute(
                """
                INSERT INTO MLModels
                    (Name, Version, OnnxPath, Mae, R2, TrainedAt, IsActive, FeatureSpecJson)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    args.name,
                    args.version,
                    onnx_abs,
                    mae,
                    r2,
                    trained_at,
                    is_active,
                    feature_spec_json,
                ),
            )
        except sqlite3.IntegrityError as ex:
            # UNIQUE (Name, Version) 위반 — 같은 버전 재등록 시도
            sys.stderr.write(
                f"INSERT failed: {ex}\n"
                f"  Name='{args.name}' Version='{args.version}' already exists.\n"
                "  → Bump --version or DELETE the existing row first.\n"
            )
            conn.rollback()
            return 1

        conn.commit()
        new_id = conn.execute("SELECT last_insert_rowid()").fetchone()[0]
        print(f"[register] inserted MLModels row id={new_id}")

        if args.activate:
            print(
                "[register] active — PredictionService 가 다음 캐시 만료(≤60s) 시점에 자동 핫스왑."
            )
        else:
            print(
                "[register] inactive — 활성화하려면 --activate 옵션으로 재실행하거나 "
                "DB 에서 IsActive=1 직접 토글."
            )

    finally:
        conn.close()

    return 0


if __name__ == "__main__":
    sys.exit(main())
