import sys
import os
import subprocess
import time

sys.path.append(os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..')))

from src.piano.mapper import RobloxPianoMapper
from src.playback.key_state_manager import KeyStateManager
from src.playback.chord_engine import ChordEngine
from src.music.events import NoteEvent

print("--- 88 KEY MAPPING CHECK ---")
m = RobloxPianoMapper()
print(f"Profile: {m.profile.name}")
pitches = sorted(m.profile.keys.keys())
print(f"Min: {m.min_pitch}")
print(f"Max: {m.max_pitch}")
print(f"Count: {len(pitches)}")
missing = [p for p in range(21, 109) if p not in pitches]
print(f"Missing: {missing}")

print("\n--- SPECIFIC MAPPINGS ---")
for pitch in [21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35]: # Low range
    k = m.map_pitch(pitch)
    if k:
        print(f"Pitch {pitch} -> Physical: {k.physical_key}, Mods: {k.modifiers}")

for pitch in [36, 37, 48, 49, 60, 61, 72, 73, 84, 85, 96]: # Middle range
    k = m.map_pitch(pitch)
    if k:
        print(f"Pitch {pitch} -> Physical: {k.physical_key}, Mods: {k.modifiers}")

for pitch in [97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108]: # High range
    k = m.map_pitch(pitch)
    if k:
        print(f"Pitch {pitch} -> Physical: {k.physical_key}, Mods: {k.modifiers}")

print("\n--- CHORD ENGINE MOCK RUN ---")
class MockBackend:
    def __init__(self):
        self.events = []
    def key_down(self, k): self.events.append(('DOWN', k, time.perf_counter()))
    def key_up(self, k): self.events.append(('UP', k, time.perf_counter()))

backend = MockBackend()
mgr = KeyStateManager(backend)
engine = ChordEngine(mgr, m)

def run_chord(notes):
    backend.events.clear()
    chord_notes = [NoteEvent(pitch=p, start_time=0, end_time=1) for p in notes]
    engine.play_chord_notes(chord_notes)
    return list(backend.events)

print("Test A (C4 E4 G4):", run_chord([60, 64, 67]))
print("Test B (C4 E4 G#4):", run_chord([60, 64, 68]))
print("Test C (A0 E1 C4 E4):", run_chord([21, 28, 60, 64]))
print("Test D (A0 E1 C4 E4 G#4):", run_chord([21, 28, 60, 64, 68]))
print("Test E (LH: A0 E1 A1, RH: C4 E4 G#4 C5):", run_chord([21, 28, 33, 60, 64, 68, 72]))

