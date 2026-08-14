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
    using an absolute tick timeline for exact event timing and serialization.
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
                    return content.startswith("MML@")
            except:
                return False
        return file_path_or_content.strip().startswith("MML@")

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
            # Ensure parent directory exists
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
        if clean_text.startswith("MML@"):
            clean_text = clean_text[4:]
        if clean_text.endswith(";"):
            clean_text = clean_text[:-1]

        tracks_mml = clean_text.split(',')
        mid = mido.MidiFile(ticks_per_beat=480)
        
        token_pattern = re.compile(
            r'\s+|([A-Ga-g][+#-]?[\d\.]*&?|[Nn]-?\d+|[Rr][\d\.]*&?|[VvTtLlOo][\d\.]*|[><])|(\S)'
        )
        
        global_tempo = 120
        total_notes = 0
        min_pitch = 127
        max_pitch = 0
        
        note_map = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}

        for track_idx, track_str in enumerate(tracks_mml):
            raw_events = [] # List of (abs_tick, priority, mido_message)
            
            octave = 4
            default_length = 4
            volume = 100
            current_tick = 0
            
            tied_pitch = -1
            tied_start_tick = -1
            tied_duration = 0
            
            def parse_length(arg_str: str) -> int:
                base_len_match = re.search(r'(\d+)', arg_str)
                if base_len_match:
                    base_len = int(base_len_match.group(1))
                else:
                    base_len = default_length
                    
                if base_len <= 0:
                    base_len = default_length
                
                dots = arg_str.count('.')
                base_ticks = int(480 * 4 / base_len)
                ticks = base_ticks
                add = base_ticks // 2
                for _ in range(dots):
                    ticks += add
                    add //= 2
                return ticks

            def emit_note(pitch: int, start_t: int, dur_t: int, vel: int):
                nonlocal total_notes, min_pitch, max_pitch
                if dur_t <= 0:
                    dur_t = 1
                # priority: 2 for note_on, 1 for note_off
                raw_events.append((start_t, 2, mido.Message('note_on', note=pitch, velocity=vel)))
                raw_events.append((start_t + dur_t, 1, mido.Message('note_off', note=pitch, velocity=0)))
                total_notes += 1
                if pitch < min_pitch:
                    min_pitch = pitch
                if pitch > max_pitch:
                    max_pitch = pitch

            pos = 0
            while pos < len(track_str):
                match = token_pattern.match(track_str, pos)
                if not match:
                    raise MmlParseError(track_idx, pos, track_str[pos], "구문 오류")
                
                valid_cmd = match.group(1)
                invalid_cmd = match.group(2)
                
                if invalid_cmd:
                    raise MmlParseError(track_idx, pos, invalid_cmd, f"지원하지 않는 토큰 '{invalid_cmd}'")
                    
                if valid_cmd:
                    cmd_char = valid_cmd[0].upper()
                    
                    if cmd_char in "ABCDEFG":
                        is_tie = valid_cmd.endswith('&')
                        clean_cmd = valid_cmd[:-1] if is_tie else valid_cmd
                        
                        pitch = (octave + 1) * 12 + note_map[cmd_char]
                        arg_idx = 1
                        if len(clean_cmd) > 1 and clean_cmd[1] in "+#-":
                            if clean_cmd[1] in '+#':
                                pitch += 1
                            elif clean_cmd[1] == '-':
                                pitch -= 1
                            arg_idx = 2
                            
                        if pitch < 0 or pitch > 127:
                            raise MmlParseError(track_idx, pos, valid_cmd, "MIDI pitch out of bounds (음고 범위 초과: 0~127)")
                            
                        dur = parse_length(clean_cmd[arg_idx:])
                        
                        if tied_pitch == pitch:
                            tied_duration += dur
                        else:
                            if tied_pitch != -1:
                                emit_note(tied_pitch, tied_start_tick, tied_duration, volume)
                            tied_pitch = pitch
                            tied_start_tick = current_tick
                            tied_duration = dur
                            
                        if not is_tie:
                            emit_note(tied_pitch, tied_start_tick, tied_duration, volume)
                            tied_pitch = -1
                            tied_start_tick = -1
                            tied_duration = 0
                            
                        current_tick += dur
                        
                    elif cmd_char == 'N':
                        # N<number> note
                        try:
                            pitch = int(valid_cmd[1:])
                        except Exception:
                            raise MmlParseError(track_idx, pos, valid_cmd, "Invalid N command format (N 명령어 형식 오류)")
                            
                        if pitch < 0 or pitch > 127:
                            raise MmlParseError(track_idx, pos, valid_cmd, "MIDI pitch out of bounds (음고 범위 초과: 0~127)")
                            
                        dur = parse_length("")
                        
                        if tied_pitch != -1:
                            emit_note(tied_pitch, tied_start_tick, tied_duration, volume)
                            tied_pitch = -1
                            tied_start_tick = -1
                            tied_duration = 0
                            
                        emit_note(pitch, current_tick, dur, volume)
                        current_tick += dur
                        
                    elif cmd_char == 'R':
                        is_tie = valid_cmd.endswith('&')
                        clean_cmd = valid_cmd[:-1] if is_tie else valid_cmd
                        dur = parse_length(clean_cmd[1:])
                        
                        if tied_pitch != -1:
                            emit_note(tied_pitch, tied_start_tick, tied_duration, volume)
                            tied_pitch = -1
                            tied_start_tick = -1
                            tied_duration = 0
                            
                        current_tick += dur
                        
                    elif cmd_char == 'O':
                        val_str = valid_cmd[1:]
                        if val_str:
                            octave = max(0, min(8, int(val_str)))
                    elif cmd_char == '>':
                        octave = min(8, octave + 1)
                    elif cmd_char == '<':
                        octave = max(0, octave - 1)
                    elif cmd_char == 'L':
                        val_str = valid_cmd[1:]
                        if val_str:
                            match_d = re.match(r'(\d+)', val_str)
                            if match_d:
                                default_length = int(match_d.group(1))
                    elif cmd_char == 'V':
                        val_str = valid_cmd[1:]
                        if val_str:
                            vol = int(val_str)
                            if vol < 0 or vol > 15:
                                raise MmlParseError(track_idx, pos, valid_cmd, "Volume must be 0-15 (볼륨 범위 초과: 0~15)")
                            volume = int(vol * 127 / 15)
                    elif cmd_char == 'T':
                        val_str = valid_cmd[1:]
                        if val_str:
                            tempo_val = int(val_str)
                            if tempo_val <= 0 or tempo_val > 500:
                                raise MmlParseError(track_idx, pos, valid_cmd, "Tempo must be > 0 (템포 범위 초과: 1~500)")
                            if track_idx == 0:
                                global_tempo = tempo_val
                                tempo_meta = mido.MetaMessage('set_tempo', tempo=mido.bpm2tempo(tempo_val))
                                # priority 0 for tempo meta
                                raw_events.append((current_tick, 0, tempo_meta))
                
                pos = match.end()
            
            if tied_pitch != -1:
                emit_note(tied_pitch, tied_start_tick, tied_duration, volume)
                
            # Serialize raw_events to MidiTrack with delta time
            track = mido.MidiTrack()
            raw_events.sort(key=lambda x: (x[0], x[1]))
            
            last_tick = 0
            for ev_tick, _, msg in raw_events:
                delta = max(0, ev_tick - last_tick)
                msg.time = delta
                track.append(msg)
                last_tick = ev_tick
                
            mid.tracks.append(track)
            
        duration_sec = mid.length
        if total_notes == 0:
            min_pitch = 0
            max_pitch = 0

        stats = {
            "tracks": len(tracks_mml),
            "tempo": global_tempo,
            "duration": duration_sec,
            "total_notes": total_notes,
            "min_pitch": min_pitch,
            "max_pitch": max_pitch,
        }
        return mid, stats
