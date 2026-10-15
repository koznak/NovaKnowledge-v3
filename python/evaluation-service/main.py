"""NovaKnowledge evaluation service.

Computes RAG quality metrics (faithfulness, answer relevance, context precision/recall)
against a golden Q&A dataset.

Two engines are available:

* **ragas** — the real metric implementations, using an LLM judge (Azure OpenAI). Used
  automatically when LLM credentials are configured.
* **offline** — a deterministic, dependency-free fallback used when no LLM credentials are
  present (CI, local demos). It is *explicitly labeled* in every response so results are
  never mistaken for real RAGAS scores.

The chosen engine is reported in every metrics payload as ``engine``.
"""

from __future__ import annotations

import asyncio
import json
import os
from pathlib import Path
from typing import Any, Optional

from fastapi import BackgroundTasks, FastAPI, HTTPException
from pydantic import BaseModel

app = FastAPI(title="NovaKnowledge Evaluation Service", version="2.0.0")

evaluation_results: dict[str, dict[str, Any]] = {}

GOLDEN_QA_FALLBACK = [
    {
        "question": "What is the PTO policy?",
        "expected_answer": "Employees receive 20 days of PTO annually.",
        "context": "Employees receive 20 days of paid time off (PTO) per year.",
    },
    {
        "question": "How do I reset my password?",
        "expected_answer": "Use the IT self-service portal.",
        "context": "Reset passwords via the IT self-service portal at it.contoso.com.",
    },
]


# --------------------------------------------------------------------------------------
# Request / response models
# --------------------------------------------------------------------------------------
class RagEvaluationRequest(BaseModel):
    run_id: str
    dataset_path: str = "tests/data/golden-qa.jsonl"
    tenant_id: str = "contoso"


class ChunkingEvaluationRequest(BaseModel):
    document_text: str
    strategies: list[str] = ["recursive", "fixed"]


class EvaluationMetrics(BaseModel):
    run_id: str
    engine: str
    faithfulness: float
    answer_relevance: float
    context_precision: float
    context_recall: float
    status: str


# --------------------------------------------------------------------------------------
# Dataset loading
# --------------------------------------------------------------------------------------
def load_golden_qa(path: str) -> list[dict[str, Any]]:
    file_path = Path(path)
    if not file_path.exists():
        file_path = Path(__file__).resolve().parents[2] / "tests" / "data" / "golden-qa.jsonl"
    if not file_path.exists():
        return GOLDEN_QA_FALLBACK

    items: list[dict[str, Any]] = []
    with open(file_path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line:
                items.append(json.loads(line))
    return items


def _normalize_dataset(dataset: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """Accept both {context} and {contexts:[...]} shapes and a retrieved answer field."""
    normalized: list[dict[str, Any]] = []
    for item in dataset:
        contexts = item.get("contexts")
        if contexts is None:
            single = item.get("context")
            contexts = [single] if single else []
        if isinstance(contexts, str):
            contexts = [contexts]

        normalized.append(
            {
                "question": item.get("question", ""),
                "answer": item.get("answer") or item.get("generated_answer") or item.get("expected_answer", ""),
                "ground_truth": item.get("ground_truth") or item.get("expected_answer", ""),
                "contexts": [c for c in contexts if c],
            }
        )
    return normalized


# --------------------------------------------------------------------------------------
# Engine selection
# --------------------------------------------------------------------------------------
def _llm_configured() -> bool:
    return bool(
        os.getenv("AZURE_OPENAI_API_KEY")
        and os.getenv("AZURE_OPENAI_ENDPOINT")
        and os.getenv("AZURE_OPENAI_CHAT_DEPLOYMENT")
    )


def _build_ragas_metrics():
    """Construct real Ragas metrics backed by an Azure OpenAI judge."""
    from langchain_openai import AzureChatOpenAI
    from ragas.llms import LangchainLLMWrapper
    from ragas.metrics import (  # type: ignore
        answer_relevancy,
        context_precision,
        context_recall,
        faithfulness,
    )

    judge = AzureChatOpenAI(
        azure_deployment=os.environ["AZURE_OPENAI_CHAT_DEPLOYMENT"],
        azure_endpoint=os.environ["AZURE_OPENAI_ENDPOINT"],
        api_key=os.environ["AZURE_OPENAI_API_KEY"],
        api_version=os.getenv("AZURE_OPENAI_API_VERSION", "2024-10-21"),
        temperature=0,
    )
    ragas_llm = LangchainLLMWrapper(judge)
    return [faithfulness, answer_relevancy, context_precision, context_recall], ragas_llm


def _compute_with_ragas(rows: list[dict[str, Any]]) -> dict[str, float]:
    """Run the real Ragas metrics and return a normalized 0..1 score map."""
    from ragas import EvaluationDataset, SingleTurnSample, evaluate  # type: ignore

    metrics, ragas_llm = _build_ragas_metrics()

    samples = [
        SingleTurnSample(
            user_input=row["question"],
            response=row["answer"],
            retrieved_contexts=row["contexts"],
            reference=row["ground_truth"],
        )
        for row in rows
    ]

    result = evaluate(
        dataset=EvaluationDataset(samples=samples),
        metrics=metrics,
        llm=ragas_llm,
        raise_exceptions=False,
    )
    frame = result.to_pandas()

    return {
        "faithfulness": float(frame["faithfulness"].mean()),
        "answer_relevance": float(frame["answer_relevancy"].mean()),
        "context_precision": float(frame["context_precision"].mean()),
        "context_recall": float(frame["context_recall"].mean()),
    }


def _overlap(expected: str, actual: str) -> float:
    expected_tokens = set(expected.lower().split())
    actual_tokens = set(actual.lower().split())
    return len(expected_tokens & actual_tokens) / max(len(expected_tokens), 1)


def _compute_offline(rows: list[dict[str, Any]]) -> dict[str, float]:
    """Deterministic fallback. Explicitly NOT Ragas; labeled as 'offline' downstream."""
    if not rows:
        return {
            "faithfulness": 0.0,
            "answer_relevance": 0.0,
            "context_precision": 0.0,
            "context_recall": 0.0,
        }

    faithfulness, relevance, precision, recall = [], [], [], []
    for row in rows:
        joined_context = " ".join(row["contexts"])
        faithfulness.append(_overlap(row["answer"], joined_context))
        relevance.append(_overlap(row["question"], row["answer"]))

        # Precision: fraction of retrieved contexts that overlap the ground truth.
        hits = sum(1 for c in row["contexts"] if _overlap(row["ground_truth"], c) > 0)
        precision.append(hits / max(len(row["contexts"]), 1))
        recall.append(min(1.0, _overlap(row["ground_truth"], joined_context)))

    def mean(values: list[float]) -> float:
        return round(sum(values) / len(values), 4)

    return {
        "faithfulness": mean(faithfulness),
        "answer_relevance": mean(relevance),
        "context_precision": mean(precision),
        "context_recall": mean(recall),
    }


def compute_metrics(dataset: list[dict[str, Any]]) -> tuple[dict[str, float], str]:
    """Return (scores, engine). Falls back to the offline engine if Ragas is unavailable."""
    rows = _normalize_dataset(dataset)
    if _llm_configured():
        try:
            return _compute_with_ragas(rows), "ragas"
        except Exception as exc:  # pragma: no cover - exercised only with live LLM creds
            app.state.last_engine_error = str(exc)
    return _compute_offline(rows), "offline"


async def run_evaluation(run_id: str, dataset_path: str) -> None:
    evaluation_results[run_id] = {"status": "running"}
    await asyncio.sleep(0)
    dataset = load_golden_qa(dataset_path)
    scores, engine = compute_metrics(dataset)
    evaluation_results[run_id] = {
        "status": "completed",
        "engine": engine,
        "sample_count": len(dataset),
        **scores,
    }


# --------------------------------------------------------------------------------------
# Endpoints
# --------------------------------------------------------------------------------------
@app.get("/")
async def root() -> dict[str, Any]:
    return {
        "service": "NovaKnowledge Evaluation Service",
        "version": "2.0.0",
        "docs": "/docs",
        "health": "/health",
        "engines": {
            "ragas": "LLM-judge Ragas metrics (requires AZURE_OPENAI_* env vars)",
            "offline": "deterministic fallback, explicitly labeled",
        },
        "endpoints": {
            "POST /evaluate/rag": "Start RAG evaluation run",
            "GET /metrics/{run_id}": "Get evaluation metrics",
            "POST /evaluate/chunking": "Compare chunking strategies",
        },
    }


@app.get("/health")
async def health() -> dict[str, str]:
    return {
        "status": "healthy",
        "service": "evaluation-service",
        "engine": "ragas" if _llm_configured() else "offline",
    }


@app.post("/evaluate/rag")
async def evaluate_rag(request: RagEvaluationRequest, background_tasks: BackgroundTasks) -> dict[str, str]:
    background_tasks.add_task(run_evaluation, request.run_id, request.dataset_path)
    return {"run_id": request.run_id, "status": "accepted"}


@app.get("/metrics/{run_id}")
async def get_metrics(run_id: str) -> dict[str, Any]:
    result = evaluation_results.get(run_id)
    if not result:
        raise HTTPException(status_code=404, detail="Evaluation run not found")
    return {"run_id": run_id, **result}


@app.post("/evaluate/chunking")
async def evaluate_chunking(request: ChunkingEvaluationRequest) -> dict[str, Any]:
    results: dict[str, dict[str, Any]] = {}
    text = request.document_text
    for strategy in request.strategies:
        if strategy == "fixed":
            chunk_size, overlap = 512, 128
            step = max(1, chunk_size - overlap)
            chunks = [text[i : i + chunk_size] for i in range(0, len(text), step)]
        else:
            paragraphs = text.split("\n\n")
            chunks, current = [], ""
            for paragraph in paragraphs:
                if len(current) + len(paragraph) < 512:
                    current = f"{current}\n\n{paragraph}".strip()
                else:
                    if current:
                        chunks.append(current)
                    current = paragraph
            if current:
                chunks.append(current)

        avg_len = sum(len(c) for c in chunks) / max(len(chunks), 1)
        results[strategy] = {
            "chunk_count": len(chunks),
            "avg_chunk_length": round(avg_len, 2),
            "score": round(min(1.0, 400 / max(avg_len, 1)), 4),
        }

    best = max(results, key=lambda name: results[name]["score"])
    return {"strategies": results, "recommended": best}
