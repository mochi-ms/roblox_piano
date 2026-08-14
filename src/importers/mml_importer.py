import re
import os
import mido
from typing import List, Tuple
from src.importers.base import BaseMusicImporter
from src.importers.midi_importer import MidiImporter
from src.music.timeline import MusicTimeline

class MmlParseError(Exception):
    def __init__(self, track_idx: int, position: int, token: str, message: str = ""):
        self.track_idx = track_idx
        self.position = position
        self.token = token
        msg = f"Track {track_idx+1}, Position {position}: Unexpected token '{token}'"
        if message:
            msg += f" ({message})"
        super().__init__(msg)

class MmlImporter(BaseMusicImporter):
    """
    Parses MML format strings/files into standard MIDI files,
    then uses MidiImporter to create a MusicTimeline.
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
        original_text = mml_text
        mml_text = mml_text.strip()
        
        if mml_text.startswith("MML@"):
            mml_text = mml_text[4:]
        if mml_text.endswith(";"):
            mml_text = mml_text[:-1]

        tracks_mml = mml_text.split(',')
        
        mid = mido.MidiFile(ticks_per_beat=480)
        
        # Tokenizer regex: matches valid commands or a single character fallback for invalid tokens
        # Valid commands:
        # A-G followed by optional +,-,# followed by optional length digits and dots, followed by optional &
        # R followed by optional length digits and dots, followed by optional &
        # V, T, L, O followed by digits and optional dots
        # >, <
        # whitespace
        # \S (anything else is invalid)
        token_pattern = re.compile(r'\s+|([A-Ga-g][+#-]?[\d\.]*&?|[Rr][\d\.]*&?|[VvTtLlOo][\d\.]*|[><])|(\S)')
        
        for track_idx, track_str in enumerate(tracks_mml):
            track = mido.MidiTrack()
            mid.tracks.append(track)
            
            # Default state
            octave = 4
            default_length = 4
            volume = 100
            current_time_ticks = 0
            
            last_event_ticks = 0
            
            def add_note_events(pitch: int, duration_ticks: int):
                nonlocal last_event_ticks
                delta_on = current_time_ticks - last_event_ticks
                track.append(mido.Message('note_on', note=pitch, velocity=volume, time=delta_on))
                last_event_ticks = current_time_ticks
                
                delta_off = duration_ticks
                track.append(mido.Message('note_off', note=pitch, velocity=0, time=delta_off))
                last_event_ticks = current_time_ticks + duration_ticks
                
            def parse_length(arg_str: str) -> int:
                ticks = 0
                base_len_match = re.search(r'(\d+)', arg_str)
                if base_len_match:
                    base_len = int(base_len_match.group(1))
                else:
                    base_len = default_length
                    
                if base_len == 0: base_len = default_length
                
                dots = arg_str.count('.')
                
                base_ticks = int(480 * 4 / base_len)
                ticks = base_ticks
                add = base_ticks // 2
                for _ in range(dots):
                    ticks += add
                    add //= 2
                return ticks

            tied_pitch = -1
            tied_duration = 0
            
            pos = 0
            while pos < len(track_str):
                match = token_pattern.match(track_str, pos)
                if not match:
                    # Should be impossible due to \S fallback
                    raise MmlParseError(track_idx, pos, track_str[pos])
                
                token = match.group(0)
                valid_cmd = match.group(1)
                invalid_cmd = match.group(2)
                
                if invalid_cmd:
                    raise MmlParseError(track_idx, pos, invalid_cmd)
                    
                if valid_cmd:
                    cmd_char = valid_cmd[0].upper()
                    
                    if cmd_char in "ABCDEFG":
                        is_tie = valid_cmd.endswith('&')
                        
                        note_map = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
                        pitch = (octave + 1) * 12 + note_map[cmd_char]
                        
                        arg_idx = 1
                        if len(valid_cmd) > 1 and valid_cmd[1] in "+#-":
                            if valid_cmd[1] in '+#': pitch += 1
                            elif valid_cmd[1] == '-': pitch -= 1
                            arg_idx = 2
                            
                        # clamp pitch to 0-127
                        if pitch < 0 or pitch > 127:
                            raise MmlParseError(track_idx, pos, valid_cmd, "MIDI pitch out of bounds (0-127)")
                            
                        duration_ticks = parse_length(valid_cmd[arg_idx:])
                        
                        if tied_pitch == pitch:
                            tied_duration += duration_ticks
                        else:
                            if tied_pitch != -1:
                                add_note_events(tied_pitch, tied_duration)
                                current_time_ticks += tied_duration
                            tied_pitch = pitch
                            tied_duration = duration_ticks
                            
                        if not is_tie:
                            add_note_events(tied_pitch, tied_duration)
                            current_time_ticks += tied_duration
                            tied_pitch = -1
                            tied_duration = 0
                            
                    elif cmd_char == 'R':
                        is_tie = valid_cmd.endswith('&')
                        duration_ticks = parse_length(valid_cmd[1:])
                        
                        if tied_pitch != -1:
                            add_note_events(tied_pitch, tied_duration)
                            current_time_ticks += tied_duration
                            tied_pitch = -1
                            tied_duration = 0
                            
                        current_time_ticks += duration_ticks
                        
                    elif cmd_char == 'O':
                        val_str = valid_cmd[1:]
                        if val_str: octave = int(val_str)
                    elif cmd_char == '>':
                        octave += 1
                    elif cmd_char == '<':
                        octave -= 1
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
                                raise MmlParseError(track_idx, pos, valid_cmd, "Volume must be 0-15")
                            volume = int(vol * 127 / 15)
                    elif cmd_char == 'T':
                        val_str = valid_cmd[1:]
                        if val_str and track_idx == 0:
                            tempo_val = int(val_str)
                            if tempo_val <= 0:
                                raise MmlParseError(track_idx, pos, valid_cmd, "Tempo must be > 0")
                            tempo = mido.bpm2tempo(tempo_val)
                            delta = current_time_ticks - last_event_ticks
                            track.append(mido.MetaMessage('set_tempo', tempo=tempo, time=delta))
                            last_event_ticks = current_time_ticks
                
                pos = match.end()
            
            if tied_pitch != -1:
                add_note_events(tied_pitch, tied_duration)
                current_time_ticks += tied_duration

        mid.save(out_filepath)

    def extract_metadata(self, mml_text: str) -> dict:
        """Utility to get tracks count, tempo, etc without saving file. Will throw MmlParseError if invalid."""
        mml_text = mml_text.strip()
        if mml_text.startswith("MML@"):
            mml_text = mml_text[4:]
        if mml_text.endswith(";"):
            mml_text = mml_text[:-1]
        
        tracks = mml_text.split(',')
        tempo = 120
        t_match = re.search(r'[Tt]\s*(\d+)', tracks[0])
        if t_match:
            tempo = int(t_match.group(1))
            
        return {
            "tracks": len(tracks),
            "tempo": tempo,
        }
