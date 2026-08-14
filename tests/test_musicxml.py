"""
Unit test for MusicXML Importer using music21
"""
import os
import tempfile
import pytest
from music21 import stream, note, chord, tempo, meter
from src.importers.musicxml_importer import MusicXmlImporter


def test_musicxml_import_roundtrip():
    # Build a tiny music21 score
    s = stream.Score()
    p1 = stream.Part()
    p1.partName = "Right Hand"
    m1 = stream.Measure()
    m1.append(tempo.MetronomeMark(number=120))
    m1.append(meter.TimeSignature('4/4'))

    n1 = note.Note('C4', quarterLength=1.0)
    ch = chord.Chord(['E4', 'G4', 'C5'], quarterLength=2.0)
    m1.append(n1)
    m1.append(ch)
    p1.append(m1)
    s.append(p1)

    with tempfile.NamedTemporaryFile(suffix=".musicxml", delete=False) as tf:
        temp_path = tf.name

    try:
        s.write('musicxml', fp=temp_path)

        importer = MusicXmlImporter()
        timeline = importer.import_score(temp_path)

        assert timeline.total_notes == 4  # 1 single note + 3 chord notes
        assert timeline.initial_bpm == 120.0
        assert timeline.notes[0].pitch == 60  # C4
    finally:
        if os.path.exists(temp_path):
            os.remove(temp_path)
