"""Tests for the NovaKnowledge evaluation service.

Run with:  pytest python/evaluation-service -q
"""

from __future__ import annotations

import sys
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, str(Path(__file__).resolve().parent))

import main  # noqa: E402


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app)


def test_root_reports_engines(client: TestClient) -> None:
    response = client.get("/")
    assert response.status_code == 200
    body = response.json()
    assert "ragas" in body["engines"]
    assert "offline" in body["engines"]


def test_health_ok(client: TestClient) -> None:
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["status"] == "healthy"


def test_normalize_accepts_context_and_contexts() -> None:
    rows = main._normalize_dataset(
        [
            {"question": "q1", "expected_answer": "a1", "context": "c1"},
            {"question": "q2", "expected_answer": "a2", "contexts": ["c2", "c3"]},
        ]
    )
    assert rows[0]["contexts"] == ["c1"]
    assert rows[1]["contexts"] == ["c2", "c3"]


def test_offline_engine_scores_in_range() -> None:
    dataset = main.load_golden_qa("does-not-exist.jsonl")
    scores, engine = main.compute_metrics(dataset)

    assert engine == "offline"  # no AZURE_OPENAI_* set in the test environment
    for value in scores.values():
        assert 0.0 <= value <= 1.0


def test_offline_engine_is_high_for_exact_match() -> None:
    dataset = [
        {
            "question": "What is the PTO policy?",
            "expected_answer": "Employees receive 20 days of PTO annually.",
            "context": "Employees receive 20 days of PTO annually.",
            "answer": "Employees receive 20 days of PTO annually.",
        }
    ]
    scores, _ = main.compute_metrics(dataset)
    assert scores["faithfulness"] == pytest.approx(1.0)
    assert scores["context_recall"] == pytest.approx(1.0)


def test_evaluate_rag_persists_completed_metrics(client: TestClient) -> None:
    response = client.post("/evaluate/rag", json={"run_id": "run-1", "dataset_path": "missing.jsonl"})
    assert response.status_code == 200

    # TestClient runs BackgroundTasks synchronously before returning.
    metrics = client.get("/metrics/run-1").json()
    assert metrics["status"] == "completed"
    assert metrics["engine"] == "offline"
    assert "faithfulness" in metrics


def test_metrics_unknown_run_returns_404(client: TestClient) -> None:
    response = client.get("/metrics/does-not-exist")
    assert response.status_code == 404


def test_chunking_recommends_a_strategy(client: TestClient) -> None:
    text = "\n\n".join(f"Paragraph {i} with some policy detail." for i in range(40))
    response = client.post("/evaluate/chunking", json={"document_text": text, "strategies": ["recursive", "fixed"]})
    assert response.status_code == 200
    body = response.json()
    assert body["recommended"] in {"recursive", "fixed"}
    assert set(body["strategies"].keys()) == {"recursive", "fixed"}
