"""5주차 §3: dbo.documents 전체를 임베딩해 data/embeddings/ 에 저장한다.

출력: vectors.npy (float32, [문서 수, 1024]), ids.npy (int64, 같은 순서의 documents.id)
사용: SEARCH_CONN 설정한 터미널에서
    python tools/embed/embed_all.py --max-seq 8192 --batch 8
"""
import argparse
import os
import time
from pathlib import Path

import numpy as np
import pyodbc
import torch
from sentence_transformers import SentenceTransformer

from measure import odbc_conn_str

OUT = Path(__file__).resolve().parents[2] / "data" / "embeddings"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="nlpai-lab/KURE-v1")
    ap.add_argument("--max-seq", type=int, required=True)
    ap.add_argument("--batch", type=int, required=True)
    args = ap.parse_args()

    ado = os.environ.get("SEARCH_CONN")
    if not ado:
        raise SystemExit("SEARCH_CONN 환경 변수가 없습니다.")
    with pyodbc.connect(odbc_conn_str(ado)) as conn:
        total = conn.execute("SELECT COUNT(*) FROM dbo.documents").fetchval()
        rows = conn.execute("SELECT id, title, body FROM dbo.documents ORDER BY id").fetchall()
    ids = np.array([r.id for r in rows], dtype=np.int64)
    texts = [f"{r.title}\n{r.body}" for r in rows]
    empty = [r.id for r in rows if not r.body.strip()]
    print(f"MSSQL COUNT {total}, 읽은 행 {len(rows)}, 빈 본문 {len(empty)}건 {empty[:10]}")

    model = SentenceTransformer(args.model, device="cuda" if torch.cuda.is_available() else "cpu")
    model.max_seq_length = args.max_seq

    t0 = time.perf_counter()
    vectors = model.encode(texts, batch_size=args.batch, convert_to_numpy=True,
                           show_progress_bar=True).astype(np.float32)
    elapsed = time.perf_counter() - t0

    norms = np.linalg.norm(vectors, axis=1)
    zero = int((norms < 1e-6).sum())  # 영벡터는 색인 전에 걸러야 한다(S1)
    OUT.mkdir(parents=True, exist_ok=True)
    np.save(OUT / "vectors.npy", vectors)
    np.save(OUT / "ids.npy", ids)

    print(f"벡터 {vectors.shape}, {elapsed:.0f}초, 장치 {model.device}, batch {args.batch}, max_seq {args.max_seq}")
    print(f"노름 최소 {norms.min():.4f} 최대 {norms.max():.4f}, 영벡터 {zero}건")
    if torch.cuda.is_available():
        print(f"VRAM 최대 텐서 {torch.cuda.max_memory_allocated() / 2**30:.2f} GiB, 예약 {torch.cuda.max_memory_reserved() / 2**30:.2f} GiB")
    print(f"저장: {OUT / 'vectors.npy'} ({(OUT / 'vectors.npy').stat().st_size:,} bytes)")


if __name__ == "__main__":
    main()
