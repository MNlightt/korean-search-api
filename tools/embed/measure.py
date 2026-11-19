"""5주차 §2: dbo.documents id 오름차순 앞 N건을 임베딩하고 시간·노름을 잰다.

사용: SEARCH_CONN 환경 변수(ADO.NET 형식, Indexer와 같은 값)를 설정한 터미널에서
    python tools/embed/measure.py --model nlpai-lab/KURE-v1 --max-seq 4096 --n 100
"""
import argparse
import os
import time

import pyodbc
import torch
from sentence_transformers import SentenceTransformer

# ADO.NET 키 -> ODBC 키. 비밀번호는 출력하지 않는다.
ODBC_KEYS = {
    "server": "SERVER", "data source": "SERVER",
    "database": "DATABASE", "initial catalog": "DATABASE",
    "user id": "UID", "uid": "UID",
    "password": "PWD", "pwd": "PWD",
    "trustservercertificate": "TrustServerCertificate",
    "encrypt": "Encrypt",
}


def odbc_conn_str(ado: str) -> str:
    parts = ["DRIVER={ODBC Driver 17 for SQL Server}"]
    for pair in filter(None, (p.strip() for p in ado.split(";"))):
        key, _, value = pair.partition("=")
        odbc_key = ODBC_KEYS.get(key.strip().lower())
        if odbc_key:
            if value.strip().lower() in ("true", "false"):
                value = "yes" if value.strip().lower() == "true" else "no"
            parts.append(f"{odbc_key}={{{value.strip()}}}")
    return ";".join(parts)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="nlpai-lab/KURE-v1")
    ap.add_argument("--max-seq", type=int, required=True)
    ap.add_argument("--n", type=int, default=100)
    ap.add_argument("--batch", type=int, default=8)
    ap.add_argument("--longest", action="store_true", help="id 순 대신 본문이 가장 긴 N건 (OOM 확인용)")
    ap.add_argument("--count-only", action="store_true", help="임베딩 없이 토큰 길이 분포만 (CPU)")
    args = ap.parse_args()
    order = "DATALENGTH(body) DESC" if args.longest else "id"

    ado = os.environ.get("SEARCH_CONN")
    if not ado:
        raise SystemExit("SEARCH_CONN 환경 변수가 없습니다.")
    with pyodbc.connect(odbc_conn_str(ado)) as conn:
        rows = conn.execute(
            f"SELECT TOP (?) id, title, body FROM dbo.documents ORDER BY {order}", args.n
        ).fetchall()
    texts = [f"{r.title}\n{r.body}" for r in rows]  # §2 입력 규칙, 접두어 없음(KURE)
    print(f"문서 {len(texts)}건, 평균 {sum(map(len, texts)) / len(texts):,.0f}자")

    if args.count_only:
        from transformers import AutoTokenizer
        tok = AutoTokenizer.from_pretrained(args.model)
        lengths = [len(ids) for ids in tok(texts, truncation=False)["input_ids"]]
        for limit in (512, 1024, 2048, 4096, 8192):
            over = [n for n in lengths if n > limit]
            print(f"> {limit:>4} 토큰: {len(over):>5}건, 잘라서 보는 토큰 합 {sum(min(n, limit) for n in lengths):>12,}")
        print(f"최대 {max(lengths):,}, 평균 {sum(lengths) / len(lengths):,.0f}")
        return

    device = "cuda" if torch.cuda.is_available() else "cpu"
    t0 = time.perf_counter()
    model = SentenceTransformer(args.model, device=device)
    model.max_seq_length = args.max_seq
    print(f"모델 로드 {time.perf_counter() - t0:.1f}초 (측정 시간에 미포함), 장치 {device}")

    model.encode(texts[:2], batch_size=2)  # 워밍업: CUDA 초기화 비용을 측정에서 뺌
    if device == "cuda":
        torch.cuda.synchronize()
        torch.cuda.reset_peak_memory_stats()

    t0 = time.perf_counter()
    emb = model.encode(texts, batch_size=args.batch, convert_to_numpy=True)
    if device == "cuda":
        torch.cuda.synchronize()
    elapsed = time.perf_counter() - t0

    print(f"{len(texts)}건 {elapsed:.2f}초 (batch {args.batch}, max_seq {args.max_seq})")
    print(f"차원 {emb.shape[1]}")
    if device == "cuda":
        print(f"VRAM 최대 텐서 {torch.cuda.max_memory_allocated() / 2**30:.2f} GiB, 예약 {torch.cuda.max_memory_reserved() / 2**30:.2f} GiB")
    norms = (emb[:3] ** 2).sum(axis=1) ** 0.5
    for r, n in zip(rows[:3], norms):
        print(f"id {r.id} 노름 {n:.4f}")
    lengths = [len(model.tokenizer(t, truncation=False)["input_ids"]) for t in texts]
    cut = sum(1 for n in lengths if n > args.max_seq)
    print(f"토큰 수 최대 {max(lengths)}, max_seq 초과(잘림) {cut}건")


if __name__ == "__main__":
    main()
