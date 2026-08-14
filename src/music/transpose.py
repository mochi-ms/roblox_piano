"""
Roblox Piano Player - Transpose Module
"""
from typing import Optional
from src.music.timeline import MusicTimeline


class Transposer:
    """
    Transposes notes in a timeline by a given semitone offset.
    Maintains original_pitch so transposition is non-destructive.
    """

    @staticmethod
    def transpose(timeline: MusicTimeline, semitones: int) -> None:
        for note in timeline.notes:
            if note.original_pitch is None:
                note.original_pitch = note.pitch
            note.pitch = note.original_pitch + semitones

    @staticmethod
    def reset_transpose(timeline: MusicTimeline) -> None:
        for note in timeline.notes:
            if note.original_pitch is not None:
                note.pitch = note.original_pitch
