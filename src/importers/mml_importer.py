import re
import os
import mido
from typing import List, Tuple, Dict, Any, Optional
from src.importers.base import BaseMusicImporter
from src.importers.midi_importer import MidiImporter
from src.music.timeline import MusicTimeline

class MmlParseError(Exception):
    def __init__(self, track_idx: int, position: int, token: str, message: str = ""):
        self.track_idx = track_idx
        self.position = position
        self.token = token
        self.custom_message = message
        msg = f"Track {track_idx+1}, Position {position}: Unexpected token '{token}'"
        if message:
            msg += f" ({message})"
        super().__init__(msg)

class MmlImporter(BaseMusicImporter):
    """
    Parses MML (Music Macro Language) format strings/files into standard MIDI files,
    using an absolute tick timeline for exact event timing and delta serialization.
    Supports multi-track, case-insensitive commands, dotted default lengths,
    standalone tie operators, N<number> pitch notes, and 64th/128th note durations.
    """
    def __init__(self):
        self.midi_importer = MidiImporter()

    def can_import(self, file_path_or_content: str) -> bool:
        if file_path_or_content.lower().endswith('.mml'):
            return True
        if os.path.exists(file_path_or_content):
            try:
                with open(file_path_or_content, 'r', encoding='utf-8') as f:
                    content = f.read(100).strip()
                    return content.upper().startswith("MML@")
            except Exception:
                return False
        return file_path_or_content.strip().upper().startswith("MML@")

    @property
    def supported_extensions(self) -> List[str]:
        return [".mml", ".txt"]

    def import_score(self, file_path_or_content: str, **kwargs) -> MusicTimeline:
        out_midi_path = kwargs.get("out_midi_path")
        if not out_midi_path:
            raise ValueError("out_midi_path must be provided to MmlImporter")

        if os.path.exists(file_path_or_content):
            with open(file_path_or_content, 'r', encoding='utf-8') as f:
                mml_text = f.read()
        else:
            mml_text = file_path_or_content

        self.convert_to_midi(mml_text, out_midi_path)
        return self.midi_importer.import_score(out_midi_path)

    def convert_to_midi(self, mml_text: str, out_filepath: str):
        mid, _ = self._parse_to_midi(mml_text)
        if out_filepath != os.devnull:
            parent = os.path.dirname(os.path.abspath(out_filepath))
            if parent:
                os.makedirs(parent, exist_ok=True)
            mid.save(out_filepath)

    def extract_metadata(self, mml_text: str) -> dict:
        """Extracts track count, tempo, duration, note count, pitch range without saving to disk."""
        _, stats = self._parse_to_midi(mml_text)
        return stats

    def _parse_to_midi(self, mml_text: str) -> Tuple[mido.MidiFile, Dict[str, Any]]:
        clean_text = mml_text.strip()
        if clean_text.upper().startswith("MML@"):
            clean_text = clean_text[4:].strip()
        if clean_text.endswith(";"):
            clean_text = clean_text[:-1].strip()

        tracks_mml = clean_text.split(',')
        mid = mido.MidiFile(ticks_per_beat=480)
        
        # Robust token pattern matching all MML commands and dialect variants
        token_regex = re.compile(
            r'\s+|'
            r'([A-Ga-g][+#-]?(?:[Ll]?\d+)?\.{0,5}&?)|'
            r'([Nn]-?\d+(?:[Ll]?\d+)?\.{0,5}&?)|'
            r'([Rr](?:[Ll]?\d+)?\.{0,5}&?)|'
            r'([Ll]\d*\.{0,5})|'
            r'([Oo]\d*)|'
            r'([><])|'
            r'([Vv]\d*)|'
            r'([Tt]\d*)|'
            r'(&)|'
            r'(\S)'
        )
        
        global_tempo_map: Dict[int, int] = {} # tick -> bpm
        global_tempo_map[0] = 120
        
        total_notes = 0
        min_pitch = 127
        max_pitch = 0
        
        note_map = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
        
        track_events_list: List[List[Tuple[int, int, Any]]] = []

        for track_idx, track_str in enumerate(tracks_mml):
            raw_events: List[Tuple[int, int, Any]] = [] # (abs_tick, priority, message)
            
            octave = 4
            default_length = 4
            default_dots = 0
            volume = 100
            current_tick = 0
            
            # Active note state for ties: [pitch, start_tick, duration_ticks, volume]
            active_note: Optional[List[int]] = None
            pending_tie = False
            
            def calc_duration(len_num: Optional[int], dots: int) -> int:
                base_len = len_num if (len_num is not None and len_num > 0) else default_length
                d_count = dots if (len_num is not None or dots > 0) else default_dots
                
                base_ticks = max(1, int(480 * 4 / base_len))
                ticks = base_ticks
                add = base_ticks // 2
                for _ in range(d_count):
                    ticks += max(1, add)
                    add //= 2
                return max(1, ticks)

            def commit_active_note():
                nonlocal active_note, total_notes, min_pitch, max_pitch, raw_events
                if active_note:
                    p, st, dur, vel = active_note
                    if dur <= 0:
                        dur = 1
                    # priority: 2 for note_on, 1 for note_off
                    raw_events.append((st, 2, mido.Message('note_on', note=p, velocity=vel)))
                    raw_events.append((st + dur, 1, mido.Message('note_off', note=p, velocity=0)))
                    total_notes += 1
                    if p < min_pitch:
                        min_pitch = p
                    if p > max_pitch:
                        max_pitch = p
                    active_note = None

            pos = 0
            while pos < len(track_str):
                match = token_regex.match(track_str, pos)
                if not match:
                    raise MmlParseError(track_idx, pos, track_str[pos], "구문 오류 (Syntax error)")
                
                # Check captured groups
                note_tok = match.group(1)
                num_note_tok = match.group(2)
                rest_tok = match.group(3)
                len_tok = match.group(4)
                oct_tok = match.group(5)
                shift_tok = match.group(6)
                vol_tok = match.group(7)
                tempo_tok = match.group(8)
                standalone_tie = match.group(9)
                invalid_tok = match.group(10)
                
                if invalid_tok:
                    raise MmlParseError(track_idx, pos, invalid_tok, f"지원하지 않는 토큰 '{invalid_tok}'")
                
                if note_tok:
                    is_tie = note_tok.endswith('&')
                    clean = note_tok[:-1] if is_tie else note_tok
                    cmd_char = clean[0].upper()
                    
                    pitch = (octave + 1) * 12 + note_map[cmd_char]
                    idx = 1
                    if len(clean) > 1 and clean[1] in "+#-":
                        if clean[1] in '+#':
                            pitch += 1
                        elif clean[1] == '-':
                            pitch -= 1
                        idx = 2
                        
                    if pitch < 0 or pitch > 127:
                        raise MmlParseError(track_idx, pos, note_tok, "MIDI pitch out of bounds (음고 범위 초과: 0~127)")
                    
                    rem = clean[idx:]
                    digits_match = re.search(r'\d+', rem)
                    len_val = int(digits_match.group(0)) if digits_match else None
                    dots_val = rem.count('.')
                    
                    dur = calc_duration(len_val, dots_val)
                    
                    if active_note and (pending_tie or is_tie) and active_note[0] == pitch:
                        active_note[2] += dur
                    else:
                        commit_active_note()
                        active_note = [pitch, current_tick, dur, volume]
                        
                    pending_tie = is_tie
                    if not is_tie and not pending_tie:
                        commit_active_note()
                        
                    current_tick += dur

                elif num_note_tok:
                    is_tie = num_note_tok.endswith('&')
                    clean = num_note_tok[:-1] if is_tie else num_note_tok
                    
                    # Pattern: N<pitch>[L<len>][dots]
                    m_n = re.match(r'[Nn](-?\d+)(?:[Ll]?(\d+))?(\.*)', clean)
                    if not m_n:
                        raise MmlParseError(track_idx, pos, num_note_tok, "Invalid N command format (N 명령어 형식 오류)")
                        
                    pitch = int(m_n.group(1))
                    if pitch < 0 or pitch > 127:
                        raise MmlParseError(track_idx, pos, num_note_tok, "MIDI pitch out of bounds (음고 범위 초과: 0~127)")
                        
                    len_str = m_n.group(2)
                    len_val = int(len_str) if len_str else None
                    dots_val = len(m_n.group(3)) if m_n.group(3) else 0
                    
                    dur = calc_duration(len_val, dots_val)
                    
                    if active_note and (pending_tie or is_tie) and active_note[0] == pitch:
                        active_note[2] += dur
                    else:
                        commit_active_note()
                        active_note = [pitch, current_tick, dur, volume]
                        
                    pending_tie = is_tie
                    if not is_tie and not pending_tie:
                        commit_active_note()
                        
                    current_tick += dur

                elif rest_tok:
                    is_tie = rest_tok.endswith('&')
                    clean = rest_tok[:-1] if is_tie else rest_tok
                    rem = clean[1:]
                    digits_match = re.search(r'\d+', rem)
                    len_val = int(digits_match.group(0)) if digits_match else None
                    dots_val = rem.count('.')
                    dur = calc_duration(len_val, dots_val)
                    
                    commit_active_note()
                    pending_tie = False
                    current_tick += dur

                elif len_tok:
                    clean = len_tok[1:]
                    digits_match = re.search(r'\d+', clean)
                    if digits_match:
                        l_val = int(digits_match.group(0))
                        if l_val > 0:
                            default_length = l_val
                            default_dots = clean.count('.')

                elif oct_tok:
                    val_str = oct_tok[1:]
                    if val_str:
                        val = int(val_str)
                        octave = max(0, min(8, val))

                elif shift_tok:
                    if shift_tok == '>':
                        octave = min(8, octave + 1)
                    elif shift_tok == '<':
                        octave = max(0, octave - 1)

                elif vol_tok:
                    val_str = vol_tok[1:]
                    if val_str:
                        v_val = int(val_str)
                        if v_val < 0 or v_val > 15:
                            raise MmlParseError(track_idx, pos, vol_tok, "Volume must be 0-15 (볼륨 범위 초과: 0~15)")
                        volume = int(v_val * 127 / 15)

                elif tempo_tok:
                    val_str = tempo_tok[1:]
                    if val_str:
                        t_val = int(val_str)
                        if t_val <= 0 or t_val > 500:
                            raise MmlParseError(track_idx, pos, tempo_tok, "Tempo must be > 0 (템포 범위 초과: 1~500)")
                        global_tempo_map[current_tick] = t_val

                elif standalone_tie:
                    if active_note is not None or len(raw_events) > 0:
                        pending_tie = True

                pos = match.end()

            commit_active_note()
            track_events_list.append(raw_events)

        # Merge Conductor Tempo events into Track 0 (or all tracks)
        tempo_items = sorted(global_tempo_map.items(), key=lambda x: x[0])
        initial_bpm = tempo_items[0][1] if tempo_items else 120

        # Build each MIDI Track
        total_duration_seconds = 0.0
        
        for track_idx, events in enumerate(track_events_list):
            track = mido.MidiTrack()
            mid.tracks.append(track)
            
            # Add tempo events to Track 0
            if track_idx == 0:
                for t_tick, t_bpm in tempo_items:
                    tempo_meta = mido.MetaMessage('set_tempo', tempo=mido.bpm2tempo(t_bpm), time=0)
                    events.append((t_tick, 0, tempo_meta))
                    
            # Sort events by tick, then priority
            events.sort(key=lambda x: (x[0], x[1]))
            
            prev_tick = 0
            for abs_tick, _, msg in events:
                delta = max(0, abs_tick - prev_tick)
                msg.time = delta
                track.append(msg)
                prev_tick = abs_tick
                
            track.append(mido.MetaMessage('end_of_track', time=0))

        # Calculate accurate duration using mido length
        total_duration_seconds = mid.length
        if min_pitch > max_pitch:
            min_pitch = 0
            max_pitch = 0

        metadata = {
            "tracks": len(tracks_mml),
            "bpm": initial_bpm,
            "tempo": initial_bpm,
            "duration": total_duration_seconds,
            "notes": total_notes,
            "total_notes": total_notes,
            "min_pitch": min_pitch,
            "max_pitch": max_pitch,
            "status": "VALID"
        }
        return mid, metadata
