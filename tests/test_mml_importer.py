import os
import pytest
from src.importers.mml_importer import MmlImporter
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
    # 1 note on, 1 note off. 
    notes = [m for m in mid.tracks[0] if m.type in ('note_on', 'note_off')]
    # Should only be one note_on and one note_off because of tie
    assert len([m for m in notes if m.type == 'note_on' and m.velocity > 0]) == 1
    
def test_mml_invalid_tokens(tmp_path):
    # Should ignore spaces and invalid tokens robustly without crashing
    mml_code = "MML@ T 1 2 0 O 4 C X Y Z 4 ;"
    importer = MmlImporter()
    out_mid = tmp_path / "test_invalid.mid"
    importer.convert_to_midi(mml_code, str(out_mid))
    
    mid = mido.MidiFile(out_mid)
    notes = [m for m in mid.tracks[0] if m.type in ('note_on', 'note_off')]
    assert len(notes) > 0

def test_can_import():
    importer = MmlImporter()
    assert importer.can_import("MML@T120C4;")
    assert importer.can_import("test.mml")
    assert not importer.can_import("test.mid")
