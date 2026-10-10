"""Assign local speaker labels to timestamped Whisper segments.

Audio never leaves the machine. Clustering combines Resemblyzer voice identity
embeddings with pitch/frequency, MFCC and spectral voice characteristics. This
makes grouping less sensitive to short phrases that have similar wording or
energy but were spoken by different people.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys

import librosa
import numpy as np
from resemblyzer import VoiceEncoder, preprocess_wav
from sklearn.cluster import AgglomerativeClustering, KMeans
from sklearn.metrics import silhouette_score
import torch


SAMPLE_RATE = 16_000
VOICE_WEIGHT = 0.90
ACOUSTIC_WEIGHT = 1.0 - VOICE_WEIGHT
SPEAKER_CHANGE_PENALTY = 0.08


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--audio", required=True)
    parser.add_argument("--segments", required=True)
    parser.add_argument("--speaker-count", default=0, type=int,
                        help="Expected speakers (2-32), or 0 to detect automatically")
    parser.add_argument("--output", required=True)
    return parser.parse_args()


def segment_audio(waveform: np.ndarray, start: float, end: float) -> np.ndarray:
    duration = len(waveform) / SAMPLE_RATE
    start = max(0.0, min(float(start), duration))
    end = max(start, min(float(end), duration))
    # Very short Whisper segments do not carry enough voice characteristics.
    # Add a small symmetric context while keeping the window local.
    if end - start < 1.0:
        middle = (start + end) / 2.0
        start = max(0.0, middle - 0.75)
        end = min(duration, middle + 0.75)
    left = int(start * SAMPLE_RATE)
    right = int(end * SAMPLE_RATE)
    return waveform[left:right]


def l2_normalize(rows: np.ndarray) -> np.ndarray:
    lengths = np.linalg.norm(rows, axis=1, keepdims=True)
    return rows / np.maximum(lengths, 1e-8)


def acoustic_features(clip: np.ndarray) -> np.ndarray:
    """Describe vocal frequency and timbre independently of loudness."""
    normalized = librosa.util.normalize(clip.astype(np.float32, copy=False))
    hop = 320
    frame = 2048
    rms = librosa.feature.rms(y=normalized, frame_length=frame, hop_length=hop)[0]
    pitch = librosa.yin(
        normalized, fmin=65.0, fmax=420.0, sr=SAMPLE_RATE,
        frame_length=frame, hop_length=hop,
    )
    voiced = pitch[np.isfinite(pitch) & (pitch >= 65.0) & (pitch <= 420.0)]
    if voiced.size:
        log_pitch = np.log(voiced)
        pitch_stats = np.array([
            np.median(log_pitch), np.std(log_pitch),
            np.percentile(log_pitch, 20), np.percentile(log_pitch, 80),
        ])
    else:
        pitch_stats = np.zeros(4)

    mfcc = librosa.feature.mfcc(
        y=normalized, sr=SAMPLE_RATE, n_mfcc=13,
        n_fft=frame, hop_length=hop,
    )
    centroid = librosa.feature.spectral_centroid(
        y=normalized, sr=SAMPLE_RATE, n_fft=frame, hop_length=hop,
    )[0] / (SAMPLE_RATE / 2.0)
    bandwidth = librosa.feature.spectral_bandwidth(
        y=normalized, sr=SAMPLE_RATE, n_fft=frame, hop_length=hop,
    )[0] / (SAMPLE_RATE / 2.0)
    rolloff = librosa.feature.spectral_rolloff(
        y=normalized, sr=SAMPLE_RATE, n_fft=frame, hop_length=hop,
    )[0] / (SAMPLE_RATE / 2.0)
    zcr = librosa.feature.zero_crossing_rate(
        normalized, frame_length=frame, hop_length=hop,
    )[0]
    spectral = np.array([
        np.mean(centroid), np.std(centroid),
        np.mean(bandwidth), np.std(bandwidth),
        np.mean(rolloff), np.std(rolloff),
        np.mean(zcr), np.std(zcr),
        np.mean(np.log(np.maximum(rms, 1e-6))), np.std(np.log(np.maximum(rms, 1e-6))),
    ])
    result = np.concatenate((pitch_stats, np.mean(mfcc, axis=1), np.std(mfcc, axis=1), spectral))
    return np.nan_to_num(result, nan=0.0, posinf=0.0, neginf=0.0).astype(np.float32)


def channel_features(channels: np.ndarray, start: float, end: float) -> np.ndarray:
    """Describe which source channel carries each voice turn.

    Stereo interviews and conference exports sometimes place people on separate
    channels. Relative energy and correlation are appended as weak evidence;
    voice identity remains dominant so ordinary stereo ambience cannot create
    fake speakers by itself.
    """
    if channels.ndim == 1 or channels.shape[0] < 2:
        return np.zeros(4, dtype=np.float32)
    duration = channels.shape[1] / SAMPLE_RATE
    left = int(max(0.0, min(float(start), duration)) * SAMPLE_RATE)
    right = int(max(float(start), min(float(end), duration)) * SAMPLE_RATE)
    clips = channels[:2, left:right]
    if clips.shape[1] < 2:
        return np.zeros(4, dtype=np.float32)
    energy = np.sqrt(np.mean(np.square(clips), axis=1) + 1e-10)
    total = float(np.sum(energy))
    shares = energy / max(total, 1e-8)
    correlation = float(np.corrcoef(clips[0], clips[1])[0, 1])
    if not np.isfinite(correlation):
        correlation = 0.0
    return np.array([shares[0], shares[1], shares[0] - shares[1], correlation], dtype=np.float32)


def combined_features(voice: list[np.ndarray], acoustic: list[np.ndarray]) -> np.ndarray:
    voice_rows = l2_normalize(np.stack(voice).astype(np.float32))
    acoustic_rows = np.stack(acoustic).astype(np.float32)
    # Robust per-recording scaling prevents one Hertz/MFCC dimension from
    # dominating while retaining relative differences between speakers.
    median = np.median(acoustic_rows, axis=0)
    scale = np.percentile(acoustic_rows, 75, axis=0) - np.percentile(acoustic_rows, 25, axis=0)
    scaled = np.clip((acoustic_rows - median) / np.maximum(scale, 1e-4), -5.0, 5.0)
    acoustic_rows = l2_normalize(scaled)
    return np.concatenate((
        voice_rows * np.sqrt(VOICE_WEIGHT),
        acoustic_rows * np.sqrt(ACOUSTIC_WEIGHT),
    ), axis=1)


def smooth_short_outliers(
    labels: np.ndarray, features: np.ndarray, valid_indices: list[int], segments: list[dict]
) -> np.ndarray:
    """Remove only low-confidence, isolated short-segment label flips."""
    result = labels.copy()
    centroids = {
        label: l2_normalize(np.mean(features[labels == label], axis=0, keepdims=True))[0]
        for label in np.unique(labels)
    }
    counts = {label: int(np.count_nonzero(labels == label)) for label in np.unique(labels)}
    for position in range(1, len(labels) - 1):
        neighbor = result[position - 1]
        if neighbor != result[position + 1] or result[position] == neighbor:
            continue
        current = result[position]
        # Keep the requested number of speaker groups intact even when one
        # speaker has only a single short turn in the recording.
        if counts[current] <= 1:
            continue
        segment = segments[valid_indices[position]]
        if float(segment["end"]) - float(segment["start"]) > 1.25:
            continue
        vector = features[position]
        current_distance = 1.0 - float(np.dot(vector, centroids[result[position]]))
        neighbor_distance = 1.0 - float(np.dot(vector, centroids[neighbor]))
        if neighbor_distance <= current_distance + 0.03:
            counts[current] -= 1
            counts[neighbor] += 1
            result[position] = neighbor
    return result


def cluster_speakers(features: np.ndarray, speaker_count: int) -> np.ndarray:
    """Cluster voices while rejecting degenerate one-clip speaker groups."""
    if speaker_count == 1:
        return np.zeros(len(features), dtype=np.int32)
    labels = AgglomerativeClustering(
        n_clusters=speaker_count, metric="cosine", linkage="average"
    ).fit_predict(features)
    counts = np.bincount(labels, minlength=speaker_count)
    minimum_group = max(2, int(np.ceil(len(features) * 0.01)))
    if int(np.min(counts)) >= minimum_group:
        return labels

    # Average-linkage can occasionally isolate a breath/noise clip as a
    # requested speaker. On unit-normalized vectors, Euclidean K-Means is
    # equivalent to cosine grouping and multiple starts provide a stable,
    # non-degenerate fallback for actual voices.
    return KMeans(
        n_clusters=speaker_count, n_init=20, random_state=0,
    ).fit_predict(features)


def detect_speaker_count(features: np.ndarray) -> int:
    """Choose a conservative speaker count from local voice/acoustic evidence."""
    if len(features) < 4:
        return 1
    maximum = min(8, len(features) - 1)
    best_count = 1
    best_score = 0.10
    for count in range(2, maximum + 1):
        labels = AgglomerativeClustering(
            n_clusters=count, metric="cosine", linkage="average"
        ).fit_predict(features)
        counts = np.bincount(labels, minlength=count)
        if int(np.min(counts)) < 2:
            continue
        score = float(silhouette_score(features, labels, metric="cosine"))
        # Prefer the simpler explanation unless an extra voice has materially
        # stronger separation. This reduces noise/breath clusters.
        adjusted = score - 0.025 * (count - 2)
        if adjusted > best_score:
            best_score = adjusted
            best_count = count
    return best_count


def stabilize_sequence(labels: np.ndarray, features: np.ndarray) -> np.ndarray:
    """Prefer stable turns unless voice evidence supports a speaker change."""
    speakers = np.unique(labels)
    centroids = l2_normalize(np.stack([
        np.mean(features[labels == speaker], axis=0) for speaker in speakers
    ]))
    emissions = 1.0 - np.clip(features @ centroids.T, -1.0, 1.0)
    count, states = emissions.shape
    costs = np.empty((count, states), dtype=np.float32)
    backtrack = np.zeros((count, states), dtype=np.int32)
    costs[0] = emissions[0]
    for position in range(1, count):
        transition_penalties = np.full(
            (states, states), SPEAKER_CHANGE_PENALTY, dtype=np.float32
        )
        np.fill_diagonal(transition_penalties, 0.0)
        transitions = costs[position - 1][:, None] + transition_penalties
        backtrack[position] = np.argmin(transitions, axis=0)
        costs[position] = emissions[position] + np.min(transitions, axis=0)

    path = np.empty(count, dtype=np.int32)
    path[-1] = int(np.argmin(costs[-1]))
    for position in range(count - 2, -1, -1):
        path[position] = backtrack[position + 1, path[position + 1]]
    stable = speakers[path]

    # Do not let temporal smoothing erase one of the requested speakers.
    for state, speaker in enumerate(speakers):
        if np.any(stable == speaker):
            continue
        best = int(np.argmin(emissions[:, state]))
        stable[best] = speaker
    return stable


def main() -> int:
    args = arguments()
    if args.speaker_count != 0 and not 2 <= args.speaker_count <= 32:
        raise ValueError("speaker-count must be 0 (automatic) or between 2 and 32")
    segments = json.loads(Path(args.segments).read_text(encoding="utf-8"))
    if not isinstance(segments, list) or not segments:
        raise ValueError("segments must be a non-empty JSON array")

    source_channels, _ = librosa.load(args.audio, sr=SAMPLE_RATE, mono=False)
    waveform = source_channels if source_channels.ndim == 1 else np.mean(source_channels, axis=0)
    if waveform.size < SAMPLE_RATE // 2:
        raise ValueError("audio is too short for speaker diarization")
    device = "cuda" if torch.cuda.is_available() else "cpu"
    encoder = VoiceEncoder(device=device)
    embeddings: list[np.ndarray] = []
    acoustics: list[np.ndarray] = []
    valid_indices: list[int] = []
    for index, segment in enumerate(segments):
        clip = segment_audio(waveform, segment["start"], segment["end"])
        if clip.size < SAMPLE_RATE // 3 or float(np.max(np.abs(clip), initial=0.0)) < 1e-4:
            continue
        prepared = preprocess_wav(clip, source_sr=SAMPLE_RATE)
        if prepared.size < SAMPLE_RATE // 3:
            continue
        embedding = encoder.embed_utterance(prepared)
        if np.all(np.isfinite(embedding)):
            embeddings.append(embedding)
            acoustics.append(np.concatenate((
                acoustic_features(clip),
                channel_features(source_channels, segment["start"], segment["end"]),
            )))
            valid_indices.append(index)

    if not embeddings:
        raise ValueError("no voiced segments available for speaker diarization")
    if args.speaker_count > 0 and len(embeddings) < args.speaker_count:
        raise ValueError("not enough voiced segments for the requested speaker count")
    features = combined_features(embeddings, acoustics)
    speaker_count = args.speaker_count or detect_speaker_count(features)
    clusters = cluster_speakers(features, speaker_count)
    clusters = stabilize_sequence(clusters, features)
    clusters = smooth_short_outliers(clusters, features, valid_indices, segments)

    # Cluster numbers are arbitrary. Rename them by first appearance so labels
    # remain stable and understandable throughout the transcript.
    label_order: dict[int, int] = {}
    valid_labels: dict[int, str] = {}
    for segment_index, cluster in zip(valid_indices, clusters, strict=True):
        cluster = int(cluster)
        if cluster not in label_order:
            label_order[cluster] = len(label_order)
        valid_labels[segment_index] = f"speaker_{label_order[cluster]}"

    valid_positions = np.asarray(valid_indices)
    result = []
    for index, segment in enumerate(segments):
        if index in valid_labels:
            speaker = valid_labels[index]
        else:
            nearest = int(valid_positions[np.argmin(np.abs(valid_positions - index))])
            speaker = valid_labels[nearest]
        result.append({"id": int(segment["id"]), "speaker": speaker})
    Path(args.output).write_text(json.dumps(result, ensure_ascii=False), encoding="utf-8")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"local diarization failed: {type(error).__name__}", file=sys.stderr)
        raise SystemExit(2)
