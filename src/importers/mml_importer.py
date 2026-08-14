import re
import os
import mido
from typing import List, Tuple
from src.importers.base import BaseMusicImporter
from src.importers.midi_importer import MidiImporter
from src.music.timeline import MusicTimeline

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
        mml_text = mml_text.strip()
        if mml_text.startswith("MML@"):
            mml_text = mml_text[4:]
        if mml_text.endswith(";"):
            mml_text = mml_text[:-1]

        # Ignore whitespaces and newlines
        mml_text = re.sub(r'\s+', '', mml_text)
        
        tracks_mml = mml_text.split(',')
        
        mid = mido.MidiFile(ticks_per_beat=480)
        
        for track_idx, track_str in enumerate(tracks_mml):
            track = mido.MidiTrack()
            mid.tracks.append(track)
            
            # Default state
            octave = 4
            default_length = 4
            volume = 100
            current_time_ticks = 0
            
            # Tokenize MML
            pattern = re.compile(r'([A-Ga-g][+#-]?|[VvTtLlOo<>]|R|r)([\d\.]*)(&?)')
            pos = 0
            
            last_event_ticks = 0
            
            def add_note_events(pitch: int, duration_ticks: int):
                nonlocal last_event_ticks
                # Note On
                delta_on = current_time_ticks - last_event_ticks
                track.append(mido.Message('note_on', note=pitch, velocity=volume, time=delta_on))
                last_event_ticks = current_time_ticks
                
                # Note Off
                delta_off = duration_ticks
                track.append(mido.Message('note_off', note=pitch, velocity=0, time=delta_off))
                last_event_ticks = current_time_ticks + duration_ticks
                
            def parse_length(length_str: str) -> int:
                if not length_str:
                    return int(480 * 4 / default_length)
                
                ticks = 0
                base_len_match = re.match(r'(\d+)', length_str)
                if base_len_match:
                    base_len = int(base_len_match.group(1))
                    dots = length_str.count('.')
                else:
                    base_len = default_length
                    dots = length_str.count('.')
                    
                if base_len == 0: base_len = default_length
                
                base_ticks = int(480 * 4 / base_len)
                ticks = base_ticks
                add = base_ticks // 2
                for _ in range(dots):
                    ticks += add
                    add //= 2
                return ticks

            tokens = pattern.finditer(track_str)
            tied_pitch = -1
            tied_duration = 0
            
            for match in tokens:
                cmd = match.group(1).upper()
                arg_str = match.group(2)
                is_tie = match.group(3) == '&'
                
                if cmd in "ABCDEFG":
                    note_map = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
                    pitch = (octave + 1) * 12 + note_map[cmd[0]]
                    if len(cmd) > 1:
                        if cmd[1] in '+#': pitch += 1
                        elif cmd[1] == '-': pitch -= 1
                        
                    # clamp pitch to 0-127
                    pitch = max(0, min(127, pitch))
                        
                    duration_ticks = parse_length(arg_str)
                    
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
                        
                elif cmd == 'R':
                    duration_ticks = parse_length(arg_str)
                    if tied_pitch != -1:
                        add_note_events(tied_pitch, tied_duration)
                        current_time_ticks += tied_duration
                        tied_pitch = -1
                        tied_duration = 0
                    current_time_ticks += duration_ticks
                    
                elif cmd == 'O':
                    if arg_str: octave = int(arg_str)
                elif cmd == '>':
                    octave += 1
                elif cmd == '<':
                    octave -= 1
                elif cmd == 'L':
                    if arg_str: default_length = int(arg_str.replace('.','')) # simplification
                elif cmd == 'V':
                    if arg_str:
                        vol = int(arg_str)
                        volume = min(127, int(vol * 127 / 15))
                elif cmd == 'T':
                    if arg_str and track_idx == 0:
                        tempo = mido.bpm2tempo(int(arg_str))
                        delta = current_time_ticks - last_event_ticks
                        track.append(mido.MetaMessage('set_tempo', tempo=tempo, time=delta))
                        last_event_ticks = current_time_ticks
            
            if tied_pitch != -1:
                add_note_events(tied_pitch, tied_duration)
                current_time_ticks += tied_duration

        mid.save(out_filepath)

    def extract_metadata(self, mml_text: str) -> dict:
        """Utility to get tracks count, tempo, etc without saving file"""
        mml_text = mml_text.strip()
        if mml_text.startswith("MML@"):
            mml_text = mml_text[4:]
        if mml_text.endswith(";"):
            mml_text = mml_text[:-1]
        
        tracks = mml_text.split(',')
        tempo = 120
        t_match = re.search(r'[Tt](\d+)', tracks[0])
        if t_match:
            tempo = int(t_match.group(1))
            
        return {
            "tracks": len(tracks),
            "tempo": tempo,
        }
