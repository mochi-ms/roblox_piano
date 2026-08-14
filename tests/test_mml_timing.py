import pytest
from src.importers.mml_importer import MmlImporter


@pytest.fixture
def importer():
    return MmlImporter()


def test_numeric_note_before_length_command(importer):
    """CASE 1: MML@T150L16N58L8GG;"""
    mml = "MML@T150L16N58L8GG;"
    mid, meta = importer._parse_to_midi(mml)
    track = mid.tracks[0]
    
    events = [msg for msg in track if msg.type in ('note_on', 'note_off')]
    # N58 (120 ticks) -> G (240 ticks) -> G (240 ticks)
    # Note on/off pairs
    assert len(events) == 6
    assert events[0].note == 58 and events[0].type == 'note_on' and events[0].time == 0
    assert events[1].note == 58 and events[1].type == 'note_off' and events[1].time == 120
    assert events[2].note == 67 and events[2].type == 'note_on' and events[2].time == 0
    assert events[3].note == 67 and events[3].type == 'note_off' and events[3].time == 240
    assert events[4].note == 67 and events[4].type == 'note_on' and events[4].time == 0
    assert events[5].note == 67 and events[5].type == 'note_off' and events[5].time == 240
    
    total_ticks = sum(msg.time for msg in track)
    assert total_ticks == 600


def test_regular_note_before_length_command(importer):
    """CASE 2: MML@T120L8CL16D;"""
    mml = "MML@T120L8CL16D;"
    mid, meta = importer._parse_to_midi(mml)
    track = mid.tracks[0]
    events = [msg for msg in track if msg.type in ('note_on', 'note_off')]
    
    assert events[0].note == 60 and events[0].type == 'note_on' and events[0].time == 0
    assert events[1].note == 60 and events[1].type == 'note_off' and events[1].time == 240
    assert events[2].note == 62 and events[2].type == 'note_on' and events[2].time == 0
    assert events[3].note == 62 and events[3].type == 'note_off' and events[3].time == 120


def test_default_length_applies_forward_only(importer):
    """CASE 3: MML@T120L16FL8G;"""
    mml = "MML@T120L16FL8G;"
    mid, meta = importer._parse_to_midi(mml)
    track = mid.tracks[0]
    events = [msg for msg in track if msg.type in ('note_on', 'note_off')]
    
    # F duration = 120, G duration = 240
    assert events[1].time == 120  # F note_off
    assert events[3].time == 240  # G note_off


def test_explicit_length_does_not_change_default(importer):
    """CASE 4: MML@T120L8C16D;"""
    mml = "MML@T120L8C16D;"
    mid, meta = importer._parse_to_midi(mml)
    track = mid.tracks[0]
    events = [msg for msg in track if msg.type in ('note_on', 'note_off')]
    
    # C16 explicit duration = 120, D default L8 = 240
    assert events[1].time == 120
    assert events[3].time == 240


def test_tie_uses_segment_lengths(importer):
    """CASE 5: MML@T120L8C&C;"""
    mml = "MML@T120L8C&C;"
    mid, meta = importer._parse_to_midi(mml)
    track = mid.tracks[0]
    events = [msg for msg in track if msg.type in ('note_on', 'note_off')]
    
    # Single continuous note C of 480 ticks
    assert len(events) == 2
    assert events[0].note == 60 and events[0].type == 'note_on' and events[0].time == 0
    assert events[1].note == 60 and events[1].type == 'note_off' and events[1].time == 480


def test_l_command_does_not_modify_previous_note(importer):
    mml = "MML@T120L4CL1"
    mid, meta = importer._parse_to_midi(mml)
    track = mid.tracks[0]
    events = [msg for msg in track if msg.type in ('note_on', 'note_off')]
    assert events[1].time == 480  # C remains 480 ticks (L4), not 1920 ticks (L1)


def test_default_length_state_transition(importer):
    mml = "MML@T120L4CL8DL16EL32F"
    mid, meta = importer._parse_to_midi(mml)
    track = mid.tracks[0]
    events = [msg for msg in track if msg.type in ('note_on', 'note_off')]
    assert events[1].time == 480  # C (L4)
    assert events[3].time == 240  # D (L8)
    assert events[5].time == 120  # E (L16)
    assert events[7].time == 60   # F (L32)


def test_default_length_multiple_transitions(importer):
    mml = "MML@T120L4.CL8.DL16.E"
    mid, meta = importer._parse_to_midi(mml)
    track = mid.tracks[0]
    events = [msg for msg in track if msg.type in ('note_on', 'note_off')]
    assert events[1].time == 720  # L4. = 480 + 240 = 720
    assert events[3].time == 360  # L8. = 240 + 120 = 360
    assert events[5].time == 180  # L16. = 120 + 60 = 180


def test_note_duration_snapshot(importer):
    mml = "MML@T120L8C>D<L16EF"
    mid, meta = importer._parse_to_midi(mml)
    track = mid.tracks[0]
    events = [msg for msg in track if msg.type in ('note_on', 'note_off')]
    assert events[1].time == 240  # C
    assert events[3].time == 240  # D
    assert events[5].time == 120  # E
    assert events[7].time == 120  # F


def test_rest_uses_current_default_length(importer):
    mml = "MML@T120L8CRCL16RD"
    mid, meta = importer._parse_to_midi(mml)
    track = mid.tracks[0]
    events = [msg for msg in track if msg.type in ('note_on', 'note_off')]
    assert events[0].time == 0    # C on
    assert events[1].time == 240  # C off
    assert events[2].time == 240  # R8 gap -> next C on at +240
    assert events[3].time == 240  # C off
    assert events[4].time == 120  # R16 gap -> next D on at +120
    assert events[5].time == 120  # D off


def test_lilac_first_length_transition(importer):
    """
    Simulate the exact snippet from Lilac:
    ...D+FGFD+DCN58L8G<G>GF4D+D<A+R2B+L16...
    Preceding state: L16, octave 4, tempo 150
    """
    snippet = "MML@T150L16N58L8G<G>GF4D+D<A+R2B+L16;"
    mid, meta = importer._parse_to_midi(snippet)
    track = mid.tracks[0]
    events = [msg for msg in track if msg.type in ('note_on', 'note_off')]
    
    # N58 duration must be 120 (L16)
    assert events[0].note == 58 and events[0].type == 'note_on'
    assert events[1].note == 58 and events[1].type == 'note_off' and events[1].time == 120
    
    # Following G duration must be 240 (L8)
    assert events[2].note == 67 and events[2].type == 'note_on' and events[2].time == 0
    assert events[3].note == 67 and events[3].type == 'note_off' and events[3].time == 240
