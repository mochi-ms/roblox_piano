import os
import pytest
from src.importers.mml_importer import MmlImporter, MmlParseError
import mido

def test_mml_metadata():
    mml_code = "MML@T131V15L16>A>C+A2A8.G+8F+8,L8C-A-B-C;"
    importer = MmlImporter()
    meta = importer.extract_metadata(mml_code)
    assert meta["tracks"] == 2
    assert meta["tempo"] == 131

def test_mml_conversion(tmp_path):
    mml_code = "MML@T131V15L16O4CDEFGAB>C<C,R4E4G4;"
    importer = MmlImporter()
    out_mid = tmp_path / "test.mid"
    importer.convert_to_midi(mml_code, str(out_mid))
    assert os.path.exists(out_mid)
    mid = mido.MidiFile(out_mid)
    assert len(mid.tracks) == 2
    
def test_mml_tie(tmp_path):
    mml_code = "MML@A2&A4;"
    importer = MmlImporter()
    out_mid = tmp_path / "test_tie.mid"
    importer.convert_to_midi(mml_code, str(out_mid))
    mid = mido.MidiFile(out_mid)
    notes = [m for m in mid.tracks[0] if m.type in ('note_on', 'note_off')]
    assert len([m for m in notes if m.type == 'note_on' and m.velocity > 0]) == 1
    
def test_mml_invalid_tokens(tmp_path):
    mml_code = "MML@ T120 O4 C X Y Z4 ;"
    importer = MmlImporter()
    out_mid = tmp_path / "test_invalid.mid"
    with pytest.raises(MmlParseError) as excinfo:
        importer.convert_to_midi(mml_code, str(out_mid))
    assert "Unexpected token 'X'" in str(excinfo.value)

def test_mml_missing_argument(tmp_path):
    # Should fallback gracefully or throw MmlParseError
    mml_code = "MML@V" # V requires an argument but the regex might catch V as valid without args, wait, the regex was [Vv][\d\.]*
    importer = MmlImporter()
    out_mid = tmp_path / "test_invalid.mid"
    # Actually V without number is valid? The regex ([VvTtLlOo][\d\.]*) captures it.
    # We should ensure it doesn't crash but behaves correctly.
    importer.convert_to_midi(mml_code, str(out_mid))
    assert os.path.exists(out_mid)

def test_mml_out_of_bounds_volume(tmp_path):
    mml_code = "MML@V16 C4;"
    importer = MmlImporter()
    out_mid = tmp_path / "test_vol.mid"
    with pytest.raises(MmlParseError) as excinfo:
        importer.convert_to_midi(mml_code, str(out_mid))
    assert "Volume must be 0-15" in str(excinfo.value)

def test_mml_out_of_bounds_tempo(tmp_path):
    mml_code = "MML@T0 C4;"
    importer = MmlImporter()
    out_mid = tmp_path / "test_tempo.mid"
    with pytest.raises(MmlParseError) as excinfo:
        importer.convert_to_midi(mml_code, str(out_mid))
    assert "Tempo must be > 0" in str(excinfo.value)

def test_can_import():
    importer = MmlImporter()
    assert importer.can_import("MML@T120C4;")
    assert importer.can_import("test.mml")
    assert not importer.can_import("test.mid")

def test_mml_all_valid_tokens(tmp_path):
    mml_code = "MML@T120 V10 L8 O4 > C+ D- E F# G A B < R4. C4&;"
    importer = MmlImporter()
    out_mid = tmp_path / "test_valid.mid"
    importer.convert_to_midi(mml_code, str(out_mid))
    mid = mido.MidiFile(out_mid)
    assert len(mid.tracks) == 1
    notes = [m for m in mid.tracks[0] if m.type in ('note_on', 'note_off')]
    assert len(notes) > 0

def test_mml_numeric_note(tmp_path):
    importer = MmlImporter()
    meta = importer.extract_metadata("MML@N60;")
    assert meta["total_notes"] == 1
    assert meta["min_pitch"] == 60
    assert meta["max_pitch"] == 60

def test_mml_numeric_note_zero(tmp_path):
    importer = MmlImporter()
    meta = importer.extract_metadata("MML@N0;")
    assert meta["min_pitch"] == 0

def test_mml_numeric_note_127(tmp_path):
    importer = MmlImporter()
    meta = importer.extract_metadata("MML@N127;")
    assert meta["max_pitch"] == 127

def test_mml_numeric_note_overflow(tmp_path):
    importer = MmlImporter()
    with pytest.raises(MmlParseError) as excinfo:
        importer.convert_to_midi("MML@N128;", os.devnull)
    assert "out of bounds" in str(excinfo.value)

def test_mml_numeric_note_negative(tmp_path):
    importer = MmlImporter()
    with pytest.raises(MmlParseError) as excinfo:
        importer.convert_to_midi("MML@N-1;", os.devnull)
    assert "out of bounds" in str(excinfo.value)

def test_mml_numeric_note_default_length(tmp_path):
    importer = MmlImporter()
    mid, _ = importer._parse_to_midi("MML@L16N60;")
    notes = [m for m in mid.tracks[0] if m.type in ('note_on', 'note_off')]
    assert len(notes) == 2

def test_mml_numeric_note_volume(tmp_path):
    importer = MmlImporter()
    mid, _ = importer._parse_to_midi("MML@V10N60;")
    note_on = [m for m in mid.tracks[0] if m.type == 'note_on' and m.velocity > 0][0]
    assert note_on.velocity == int(10 * 127 / 15)

def test_mml_metadata_duration(tmp_path):
    importer = MmlImporter()
    meta = importer.extract_metadata("MML@T120L4C;")
    assert abs(meta["duration"] - 0.5) < 0.05

def test_mml_metadata_note_count(tmp_path):
    importer = MmlImporter()
    meta = importer.extract_metadata("MML@CDEFGAB;")
    assert meta["total_notes"] == 7

