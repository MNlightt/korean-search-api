from sentence_transformers import SentenceTransformer
from fastapi import FastAPI
import torch
import numpy as np
import logging
import time

app = FastAPI()

logging.basicConfig(level=logging.INFO,
                    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

model_name = "nlpai-lab/KURE-v1"
max_seq_length = 4096

model = SentenceTransformer(model_name, device="cuda" if torch.cuda.is_available() else "cpu")
model.max_seq_length = max_seq_length

@app.get("/embed")
def embed_query(q: str):
    start = time.perf_counter()
    vector = model.encode([q], convert_to_numpy=True).astype(np.float32)[0]
    elapsed_ms = (time.perf_counter() - start) * 1000
    logger.info("embed len=%d elapsed_ms=%.1f", len(q), elapsed_ms)
    return {"embed":vector.tolist()}
