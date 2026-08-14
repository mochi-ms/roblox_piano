"""
Unit tests for MIDI, MusicXML, and Numeric notation importers
"""
import os
import tempfile
import mido
import pytest
from src.importers.midi_importer import MidiImporter
from src.importers.numeric_importer import NumericImporter


def test_numeric_importer_single_notes_and_chords():
    importer = NumericImporter()
    score_text = "1 3 5 [1 3 5] - 0 1' 1,"

    timeline = importer.import_score(score_text, tonic="C", base_octave=4, bpm=120.0)

    assert timeline.total_notes == 8  # 1, 3, 5 (3) + [1, 3, 5] (3) + 1' (1) + 1, (1)
    pitches = [n.pitch for n in timeline.notes]

    # C4 = 60, E4 = 64, G4 = 67
    assert pitches[0] == 60  # 1
    assert pitches[1] == 64  # 3
    assert pitches[2] == 67  # 5
    assert pitches[3] == 60  # [1
    assert pitches[4] == 64  # 3
    assert pitches[5] == 67  # 5]
    assert pitches[6] == 72  # 1' (C5)
    assert pitches[7] == 48  # 1, (C3)


def test_numeric_importer_accidentals():
    importer = NumericImporter()
    score_text = "#4 b3"
    timeline = importer.import_score(score_text, tonic="C", base_octave=4)
    pitches = [n.pitch for n in timeline.notes]

    # In C Major: 4 = F4 (65), #4 = F#4 (66)
    assert pitches[0] == 66
    # 3 = E4 (64), b3 = Eb4 (63)
    assert pitches[1] == 63


def test_midi_importer_synthetic_file():
    importer = MidiImporter()

    # Create temporary MIDI file
    mid = mido.MidiFile(ticks_per_beat=480)
    track = mido.MidiTrack()
    mid.tracks.append(track)

    track.append(mido.MetaMessage('set_tempo', tempo=mido.bpm2tempo(120), time=0))
    track.append(mido.Message('note_on', note=60, velocity=64, time=0))
    track.append(mido.Message('note_off', note=60, velocity=64, time=480))
    track.append(mido.Message('note_on', note=64, velocity=64, time=0))
    track.append(mido.Message('note_off', note=64, velocity=64, time=480))

    with tempfile.NamedTemporaryFile(suffix=".mid", delete=False) as tf:
        temp_path = tf.name

    try:
        mid.save(temp_path)
        timeline = importer.import_score(temp_path)

        assert timeline.total_notes == 2
        assert timeline.initial_bpm == 120.0
        assert timeline.notes[0].pitch == 60
        assert abs(timeline.notes[0].start_time - 0.0) < 0.01
        assert abs(timeline.notes[0].duration - 0.5) < 0.01
        assert timeline.notes[1].pitch == 64
        assert abs(timeline.notes[1].start_time - 0.5) < 0.01
    finally:
        if os.path.exists(temp_path):
            os.remove(temp_path)
