#!/usr/bin/env python3
"""Summarize Tarkov Performance Suite CSV captures without modifying them."""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
import pandas as pd


TIMING_COLUMNS = [
    "frame_time_ms",
    "main_thread_ms",
    "render_thread_ms",
    "cpu_total_ms",
    "gpu_frame_ms",
    "frame_time_gpu_ms",
    "gfx_wait_for_present_ms",
    "player_loop_ms",
    "wait_for_target_fps_ms",
    "gc_collect_ms",
]

COUNT_COLUMNS = [
    "player_count",
    "ai_count",
    "visible_ai_count",
    "corpse_count",
    "animator_count",
    "skinned_renderer_count",
    "shadow_renderer_count",
    "shadow_effective_distance",
    "shadow_disabled_renderer_count",
    "skinning_modified_renderer_count",
    "draw_calls",
    "setpass_calls",
    "fika_server_fps",
]


def numeric(frame: pd.DataFrame) -> pd.DataFrame:
    for column in set(TIMING_COLUMNS + COUNT_COLUMNS + ["fps", "timestamp"]):
        if column in frame:
            frame[column] = pd.to_numeric(frame[column], errors="coerce")
    # ProfilerRecorder can occasionally expose an invalid sentinel-sized GPU
    # sample while the underlying counter resets.  Keep it out of summaries.
    if "gpu_frame_ms" in frame:
        frame.loc[frame["gpu_frame_ms"] > 1000, "gpu_frame_ms"] = np.nan
    return frame


def fmt(value: float) -> str:
    return "n/a" if pd.isna(value) else f"{value:.3f}"


def summarize(path: Path) -> pd.DataFrame:
    frame = numeric(pd.read_csv(path))
    duration = frame["timestamp"].max() if "timestamp" in frame else np.nan
    print(f"\n=== {path.name} ===")
    print(
        f"samples={len(frame)} duration={fmt(duration)}s "
        f"fps avg={fmt(frame['fps'].mean())} median={fmt(frame['fps'].median())} "
        f"p5={fmt(frame['fps'].quantile(0.05))} min={fmt(frame['fps'].min())}"
    )
    print(
        f"frames below 50/40/30 FPS: "
        f"{int((frame['fps'] < 50).sum())}/{int((frame['fps'] < 40).sum())}/{int((frame['fps'] < 30).sum())}"
    )

    # Single frames reveal hitches; a one-second rolling frame-time average
    # distinguishes those from a sustained 30-40 FPS slowdown.
    elapsed = pd.to_timedelta(frame["timestamp"], unit="s")
    rolling = frame.set_index(elapsed)["frame_time_ms"].rolling("1s", min_periods=10).mean()
    rolling_fps = 1000.0 / rolling
    print(
        f"1s rolling FPS min={fmt(rolling_fps.min())} "
        f"p5={fmt(rolling_fps.quantile(0.05))} "
        f"samples below 50/40/30={int((rolling_fps < 50).sum())}/"
        f"{int((rolling_fps < 40).sum())}/{int((rolling_fps < 30).sum())}"
    )

    stable = frame[frame["frame_time_ms"].between(1, 100)].copy()
    if "main_thread_ms" in stable and stable["main_thread_ms"].notna().any():
        cpu_ms = stable["main_thread_ms"].dropna().median()
        gpu_ms = stable["gpu_frame_ms"].dropna().median() if "gpu_frame_ms" in stable else np.nan
        print(f"sustained median CPU/GPU budget={fmt(cpu_ms)}/{fmt(gpu_ms)} ms")
        for target_fps in (60, 80, 100):
            target_ms = 1000.0 / target_fps
            saving = max(0.0, cpu_ms - target_ms)
            percent = saving / cpu_ms * 100.0 if cpu_ms > 0 else np.nan
            print(f"CPU saving needed for {target_fps} FPS: {fmt(saving)} ms ({fmt(percent)}%)")
        if gpu_ms > 0:
            print(f"same-workload GPU ceiling if CPU were free: ~{1000.0 / gpu_ms:.1f} FPS")

    for limit in (50, 40, 30):
        below = frame["fps"] < limit
        groups = below.ne(below.shift()).cumsum()
        longest = int(below.groupby(groups).sum().max()) if len(frame) else 0
        print(f"longest consecutive raw run below {limit} FPS: {longest} frame(s)")

    rows = []
    for column in TIMING_COLUMNS:
        if column not in frame or frame[column].notna().sum() == 0:
            continue
        series = frame[column].dropna()
        rows.append(
            {
                "metric": column,
                "avg": series.mean(),
                "p95": series.quantile(0.95),
                "p99": series.quantile(0.99),
                "max": series.max(),
            }
        )
    print(pd.DataFrame(rows).to_string(index=False, float_format=lambda x: f"{x:.3f}"))

    threshold = frame["frame_time_ms"].quantile(0.95)
    worst = frame[frame["frame_time_ms"] >= threshold]
    normal = frame[frame["frame_time_ms"] < threshold]
    comparisons = []
    for column in COUNT_COLUMNS + TIMING_COLUMNS[1:]:
        if column not in frame or frame[column].notna().sum() == 0:
            continue
        comparisons.append(
            {
                "metric": column,
                "normal_avg": normal[column].mean(),
                "worst5_avg": worst[column].mean(),
                "delta": worst[column].mean() - normal[column].mean(),
                "corr_frame": frame["frame_time_ms"].corr(frame[column]),
            }
        )
    comparison = pd.DataFrame(comparisons).sort_values("corr_frame", ascending=False)
    print("Worst 5% comparison (sorted by correlation with frame time):")
    print(comparison.to_string(index=False, float_format=lambda x: f"{x:.3f}"))

    spikes = frame.nlargest(8, "frame_time_ms")
    spike_columns = [
        column
        for column in [
            "timestamp", "fps", "frame_time_ms", "main_thread_ms", "render_thread_ms",
            "cpu_total_ms", "gpu_frame_ms", "player_count", "ai_count", "visible_ai_count",
            "draw_calls", "setpass_calls", "fika_server_fps",
        ]
        if column in spikes
    ]
    print("Largest frame spikes:")
    print(spikes[spike_columns].to_string(index=False, float_format=lambda x: f"{x:.3f}"))

    if "optimizations_enabled" in frame:
        state = frame["optimizations_enabled"].astype(str).str.lower().map(
            {"true": True, "false": False, "1": True, "0": False}
        )
        valid = frame[state.notna()].copy()
        valid["ab_state"] = state[state.notna()].astype(bool).to_numpy()
        if valid["ab_state"].nunique() > 1:
            # Drop five seconds after every switch so texture restoration, camera recreation,
            # and player movement immediately around the key press do not decide the result.
            valid["ab_segment"] = valid["ab_state"].ne(valid["ab_state"].shift()).cumsum()
            segment_start = valid.groupby("ab_segment")["timestamp"].transform("min")
            settled = valid[(valid["timestamp"] - segment_start) >= 5.0]
            rows = []
            for enabled, group in settled.groupby("ab_state"):
                rows.append({
                    "state": "ON" if enabled else "OFF",
                    "samples": len(group),
                    "seconds": group["frame_time_ms"].sum() / 1000.0,
                    "effective_fps": 1000.0 / group["frame_time_ms"].mean(),
                    "fps_p5": group["fps"].quantile(0.05),
                    "frame_p95_ms": group["frame_time_ms"].quantile(0.95),
                    "main_avg_ms": group["main_thread_ms"].mean() if "main_thread_ms" in group else np.nan,
                    "gpu_avg_ms": group["gpu_frame_ms"].mean() if "gpu_frame_ms" in group else np.nan,
                })
            print("A/B comparison (first 5 seconds after each switch excluded):")
            print(pd.DataFrame(rows).to_string(index=False, float_format=lambda x: f"{x:.3f}"))
    frame["capture"] = path.stem
    return frame


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("paths", nargs="+", type=Path)
    args = parser.parse_args()
    captures = [summarize(path) for path in args.paths]
    combined = pd.concat(captures, ignore_index=True)
    print("\n=== COMBINED ===")
    print(
        f"samples={len(combined)} fps avg={fmt(combined['fps'].mean())} "
        f"p5={fmt(combined['fps'].quantile(0.05))} min={fmt(combined['fps'].min())} "
        f"frame p95={fmt(combined['frame_time_ms'].quantile(0.95))}ms "
        f"p99={fmt(combined['frame_time_ms'].quantile(0.99))}ms"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
