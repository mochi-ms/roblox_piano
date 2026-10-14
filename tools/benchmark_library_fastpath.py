"""
Temporary benchmark for the V1 Library fast path.

This does NOT touch the user's real library.
Run:
    python tools/benchmark_library_fastpath.py
"""
import os
import sys

# Ensure repository root is on sys.path
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

import sqlite3
import tempfile
import time
import uuid

from src.library.database import ScoreDatabase


def percentile(values, p):
    values = sorted(values)
    if not values:
        return 0.0
    index = min(len(values) - 1, int((len(values) - 1) * p))
    return values[index]


def main():
    with tempfile.TemporaryDirectory(prefix="rp_library_bench_") as temp_dir:
        db_path = os.path.join(temp_dir, "library.db")
        db = ScoreDatabase(db_path)

        rows = []
        now = time.time()
        for i in range(10_000):
            folder_id = None if i < 5_000 else "folder-b"
            rows.append(
                (
                    str(uuid.uuid4()),
                    f"Benchmark Song {i:05}",
                    "MIDI",
                    "",
                    os.path.join(temp_dir, f"{i}.mid"),
                    f"{i}.mid",
                    ".mid",
                    folder_id,
                    120.0,
                    120.0,
                    100,
                    "benchmark",
                    "READY",
                    "",
                    0,
                    now,
                    0.0,
                    0.0,
                )
            )

        conn = sqlite3.connect(db_path)
        try:
            conn.executemany(
                """
                INSERT INTO scores
                (id, title, source_type, source_url, filepath, original_filename,
                 file_extension, folder_id, duration, bpm, total_notes, tags,
                 analysis_status, analysis_error, favorite, created_at, updated_at,
                 last_played_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                rows,
            )
            conn.commit()
        finally:
            conn.close()

        samples = []
        for _ in range(100):
            started = time.perf_counter()
            result = db.get_scores_by_folder(
                None,
                limit=250,
                offset=0,
                sort_by="title",
                descending=False,
            )
            samples.append((time.perf_counter() - started) * 1000)
            assert len(result) == 250

        search_samples = []
        for _ in range(30):
            started = time.perf_counter()
            result = db.search_scores(
                "Song 09",
                limit=250,
                offset=0,
                sort_by="title",
                descending=False,
            )
            search_samples.append((time.perf_counter() - started) * 1000)
            assert result

        print("Synthetic library: 10,000 scores")
        print(
            "Folder page query (250 rows): "
            f"median={percentile(samples, 0.50):.2f}ms "
            f"p95={percentile(samples, 0.95):.2f}ms"
        )
        print(
            "LIKE search page (250 rows): "
            f"median={percentile(search_samples, 0.50):.2f}ms "
            f"p95={percentile(search_samples, 0.95):.2f}ms"
        )


if __name__ == "__main__":
    main()
