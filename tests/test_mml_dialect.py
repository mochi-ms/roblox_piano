import pytest
import mido
import os
import tempfile
from src.importers.mml_importer import MmlImporter, MmlParseError
from src.services.mml_service import MmlConversionService

def test_mml_lowercase():
    importer = MmlImporter()
    mml_upper = "MML@T120V15L8O4CDEFG;"
    mml_lower = "mml@t120v15l8o4cdefg;"
    
    meta_u = importer.extract_metadata(mml_upper)
    meta_l = importer.extract_metadata(mml_lower)
    
    assert meta_u['notes'] == meta_l['notes'] == 5
    assert meta_u['duration'] == meta_l['duration']
    assert meta_u['bpm'] == meta_l['bpm'] == 120

def test_mml_dotted_default_length():
    importer = MmlImporter()
    # 120 BPM: quarter note = 0.5s, dotted quarter = 0.75s
    meta_l4 = importer.extract_metadata("MML@T120L4C;")
    meta_l4_dot = importer.extract_metadata("MML@T120L4.C;")
    
    assert meta_l4['notes'] == 1
    assert meta_l4_dot['notes'] == 1
    assert pytest.approx(meta_l4['duration'], 0.01) == 0.5
    assert pytest.approx(meta_l4_dot['duration'], 0.01) == 0.75

def test_mml_standalone_tie():
    importer = MmlImporter()
    # C4&C4 -> single note of duration 1.0s (120 BPM)
    # C4 & C4 (standalone &) -> single note of duration 1.0s
    meta_tied = importer.extract_metadata("MML@T120L4C&C;")
    meta_separate = importer.extract_metadata("MML@T120L4C C;")
    
    assert meta_tied['notes'] == 1
    assert pytest.approx(meta_tied['duration'], 0.01) == 1.0
    assert meta_separate['notes'] == 2
    assert pytest.approx(meta_separate['duration'], 0.01) == 1.0

def test_mml_numeric_note_tie_and_length():
    importer = MmlImporter()
    # N25L4.&N25
    meta = importer.extract_metadata("MML@T120L4N60L4.&N60;")
    assert meta['notes'] == 1
    # 120 BPM: dotted quarter (0.75) + quarter (0.5) = 1.25s
    assert pytest.approx(meta['duration'], 0.01) == 1.25

def test_mml_multitrack_with_duplicate_tempo():
    importer = MmlImporter()
    mml = "MML@T120L4CDEF,T120L4O3CDEF,T120L4O2CDEF;"
    meta = importer.extract_metadata(mml)
    assert meta['tracks'] == 3
    assert meta['notes'] == 12
    assert meta['bpm'] == 120

def test_mml_64th_notes():
    importer = MmlImporter()
    mml = "MML@T120L64CDEF GAB>C;"
    meta = importer.extract_metadata(mml)
    assert meta['notes'] == 8
    assert meta['duration'] > 0
