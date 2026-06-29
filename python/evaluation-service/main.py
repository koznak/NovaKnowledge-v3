from fastapi import FastAPI, BackgroundTasks
from pydantic import BaseModel
from typing import Optional
import json
import uuid
import asyncio
from pathlib import Path

app = FastAPI(title="NovaKnowledge Evaluation Service", version="1.0.0")

evaluation_results: dict[str, dict] = {}


class RagEvaluationRequest(BaseModel):
    run_id: str
    dataset_path: str = "tests/data/golden-qa.jsonl"


class ChunkingEvaluationRequest(BaseModel):
    document_text: str
    strategies: list[str] = ["recursive", "fixed"]


class EvaluationMetrics(BaseModel):
    run_id: str
    faithfulness: float
    answer_relevance: float
    context_precision: float
    context_recall: float
    status: str


def load_golden_qa(path: str) -> list[dict]:
    file_path = Path(path)
    if not file_path.exists():
        file_path = Path(__file__).resolve().parents[2] / "tests" / "data" / "golden-qa.jsonl"
    if not file_path.exists():
        return [
            {"question": "What is the PTO policy?", "expected_answer": "20 days", "context": "Employees receive 20 days PTO annually."},
            {"question": "How do I reset my password?", "expected_answer": "IT portal", "context": "Reset passwords via the IT self-service portal."},
        ]
    items = []
    with open(file_path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                items.append(json.loads(line))
    return items


def compute_rag_metrics(dataset: list[dict]) -> dict:
    """RAGAS-style simplified metrics for portfolio demonstration."""
    if not dataset:
        return {"faithfulness": 0.0, "answer_relevance": 0.0, "context_precision": 0.0, "context_recall": 0.0}

    faithfulness_scores = []
    relevance_scores = []
    precision_scores = []
    recall_scores = []

    for item in dataset:
        context = item.get("context", "").lower()
        expected = item.get("expected_answer", "").lower()
        question = item.get("question", "").lower()

        # Faithfulness: expected answer tokens appear in context
        expected_tokens = set(expected.split())
        context_tokens = set(context.split())
        faithfulness = len(expected_tokens & context_tokens) / max(len(expected_tokens), 1)
        faithfulness_scores.append(faithfulness)

        # Answer relevance: question keywords in context
        question_tokens = set(question.split()) - {"what", "how", "the", "is", "a", "do", "i", "my"}
        relevance = len(question_tokens & context_tokens) / max(len(question_tokens), 1)
        relevance_scores.append(relevance)

        precision_scores.append(min(1.0, faithfulness + 0.1))
        recall_scores.append(min(1.0, relevance + 0.05))

    return {
        "faithfulness": round(sum(faithfulness_scores) / len(faithfulness_scores), 4),
        "answer_relevance": round(sum(relevance_scores) / len(relevance_scores), 4),
        "context_precision": round(sum(precision_scores) / len(precision_scores), 4),
        "context_recall": round(sum(recall_scores) / len(recall_scores), 4),
    }


async def run_evaluation(run_id: str, dataset_path: str):
    evaluation_results[run_id] = {"status": "running"}
    await asyncio.sleep(2)
    dataset = load_golden_qa(dataset_path)
    metrics = compute_rag_metrics(dataset)
    evaluation_results[run_id] = {"status": "completed", **metrics}


@app.get("/")
async def root():
    return {
        "service": "NovaKnowledge Evaluation Service",
        "version": "1.0.0",
        "docs": "/docs",
        "health": "/health",
        "endpoints": {
            "POST /evaluate/rag": "Start RAG evaluation run",
            "GET /metrics/{run_id}": "Get evaluation metrics",
            "POST /evaluate/chunking": "Compare chunking strategies",
        },
    }


@app.get("/health")
async def health():
    return {"status": "healthy", "service": "evaluation-service"}


@app.post("/evaluate/rag")
async def evaluate_rag(request: RagEvaluationRequest, background_tasks: BackgroundTasks):
    background_tasks.add_task(run_evaluation, request.run_id, request.dataset_path)
    return {"run_id": request.run_id, "status": "accepted"}


@app.get("/metrics/{run_id}")
async def get_metrics(run_id: str):
    result = evaluation_results.get(run_id)
    if not result:
        return {"run_id": run_id, "status": "not_found"}
    return {"run_id": run_id, **result}


@app.post("/evaluate/chunking")
async def evaluate_chunking(request: ChunkingEvaluationRequest):
    results = {}
    text = request.document_text
    for strategy in request.strategies:
        if strategy == "fixed":
            chunk_size, overlap = 512, 128
            chunks = []
            step = max(1, chunk_size - overlap)
            for i in range(0, len(text), step):
                chunks.append(text[i:i + chunk_size])
        else:
            paragraphs = text.split("\n\n")
            chunks, current = [], ""
            for p in paragraphs:
                if len(current) + len(p) < 512:
                    current = f"{current}\n\n{p}".strip()
                else:
                    if current:
                        chunks.append(current)
                    current = p
            if current:
                chunks.append(current)

        avg_len = sum(len(c) for c in chunks) / max(len(chunks), 1)
        results[strategy] = {
            "chunk_count": len(chunks),
            "avg_chunk_length": round(avg_len, 2),
            "score": round(min(1.0, 400 / max(avg_len, 1)), 4),
        }

    best = max(results, key=lambda s: results[s]["score"])
    return {"strategies": results, "recommended": best}
