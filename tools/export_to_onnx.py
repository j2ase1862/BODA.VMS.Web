#!/usr/bin/env python3
"""
Predictive_DefectRate_Plan Phase D — LightGBM (.txt) → ONNX export + 자체 검증.

Usage:
    python tools/export_to_onnx.py \\
        --model models/baseline.txt \\
        --out   models/baseline.onnx \\
        [--input-name input]        \\
        [--opset 15]                \\
        [--tolerance 1e-4]

Inputs:
    models/{name}.txt — LightGBM native (saved by tools/train_baseline.py)

Outputs:
    models/{name}.onnx — ONNX 그래프
        • input name : 기본 "input"  (PredictionService.RunInference 와 호환 — InputNames.First() 사용)
        • input shape: [None, num_features]  dtype=float32
        • output     : [None, 1] 회귀값 (LightGBM 회귀의 onnxmltools 표준 형식)

Self-check:
    같은 입력에 대해 LightGBM Booster.predict() 와 ONNX Runtime 추론 결과를
    np.allclose(atol=tolerance) 비교. 불일치하면 종료 코드 != 0 + diff 보고.
    → 변환된 ONNX 가 실제로 동일 모델임을 보장. Phase E 추론과 동일한 결과 보장.

Notes:
    • float32 컨버전 손실로 인해 1e-5 ~ 1e-4 수준의 차이는 정상.
    • 카테고리 피처는 LightGBM 내부에서 정수 인코딩 — float32 입력에 정수가 들어와도
      ONNX 그래프가 동일하게 처리(onnxmltools 의 LightGBM converter 가 caveat 없이 지원).
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np

try:
    import lightgbm as lgb
except ImportError:
    sys.stderr.write("missing dep: pip install lightgbm\n")
    sys.exit(1)

try:
    import onnxmltools
    from onnxmltools.convert.common.data_types import FloatTensorType
except ImportError:
    sys.stderr.write("missing dep: pip install onnxmltools\n")
    sys.exit(1)

try:
    import onnxruntime as ort
except ImportError:
    sys.stderr.write("missing dep: pip install onnxruntime\n")
    sys.exit(1)


RNG_SEED = 42


def convert(booster: lgb.Booster, input_name: str, opset: int) -> bytes:
    n_features = booster.num_feature()
    initial_types = [(input_name, FloatTensorType([None, n_features]))]
    onnx_model = onnxmltools.convert_lightgbm(
        booster,
        initial_types=initial_types,
        target_opset=opset,
    )
    return onnx_model.SerializeToString()


def verify(
    booster: lgb.Booster,
    onnx_bytes: bytes,
    input_name: str,
    n_samples: int = 64,
    tolerance: float = 1e-4,
) -> tuple[bool, float, float]:
    """LightGBM native vs ONNX Runtime 추론 결과 비교.

    Returns (ok, max_abs_diff, mean_abs_diff).
    """
    n_features = booster.num_feature()
    rng = np.random.default_rng(RNG_SEED)
    # 다양성 확보 — 균등(피처 스케일 다름 무관)에 NaN 일부 섞기
    x = rng.standard_normal((n_samples, n_features)).astype(np.float32)
    # 10% NaN — LightGBM ONNX 가 missing handling 유지하는지 확인
    nan_mask = rng.random(x.shape) < 0.10
    x[nan_mask] = np.nan

    pred_lgb = booster.predict(x).reshape(-1).astype(np.float64)

    sess = ort.InferenceSession(onnx_bytes, providers=["CPUExecutionProvider"])
    onnx_inputs = {input_name: x}
    pred_onnx = sess.run(None, onnx_inputs)[0].reshape(-1).astype(np.float64)

    diff = np.abs(pred_lgb - pred_onnx)
    max_diff = float(diff.max())
    mean_diff = float(diff.mean())
    ok = max_diff <= tolerance
    return ok, max_diff, mean_diff


def main() -> int:
    ap = argparse.ArgumentParser(
        description=__doc__,
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--model", required=True, help="Input LightGBM .txt path")
    ap.add_argument("--out", required=True, help="Output .onnx path")
    ap.add_argument(
        "--input-name",
        default="input",
        help="ONNX input tensor name (default: 'input' — PredictionService 호환)",
    )
    ap.add_argument(
        "--opset",
        type=int,
        default=15,
        help="ONNX opset version (default: 15)",
    )
    ap.add_argument(
        "--tolerance",
        type=float,
        default=1e-4,
        help="Max absolute prediction diff allowed (default: 1e-4)",
    )
    ap.add_argument(
        "--samples",
        type=int,
        default=64,
        help="Number of random samples for verification (default: 64)",
    )
    args = ap.parse_args()

    model_path = Path(args.model)
    if not model_path.exists():
        sys.stderr.write(f"model not found: {model_path}\n")
        return 1

    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    print(f"[export-onnx] loading {model_path}")
    booster = lgb.Booster(model_file=str(model_path))
    n_features = booster.num_feature()
    print(f"[export-onnx] features: {n_features}")

    print(f"[export-onnx] converting (opset={args.opset}, input='{args.input_name}')")
    onnx_bytes = convert(booster, args.input_name, args.opset)

    out_path.write_bytes(onnx_bytes)
    print(f"[export-onnx] wrote {len(onnx_bytes):,} bytes → {out_path}")

    print(f"[export-onnx] verifying — {args.samples} samples, tolerance={args.tolerance}")
    ok, max_diff, mean_diff = verify(
        booster, onnx_bytes, args.input_name, args.samples, args.tolerance
    )
    print(f"[export-onnx]   max abs diff : {max_diff:.6e}")
    print(f"[export-onnx]   mean abs diff: {mean_diff:.6e}")

    if not ok:
        sys.stderr.write(
            f"[export-onnx] FAIL — diff exceeds tolerance "
            f"({max_diff:.6e} > {args.tolerance:.6e})\n"
            "  → ONNX 변환이 모델 의미를 보존하지 못함. "
            "opset/입력 dtype/카테고리 처리 확인.\n"
        )
        return 2

    print(f"[export-onnx] OK — ONNX 추론이 LightGBM native 와 일치 (tol={args.tolerance})")
    print(f"[export-onnx] next: python tools/register_model.py --onnx {out_path} --activate ...")
    return 0


if __name__ == "__main__":
    sys.exit(main())
