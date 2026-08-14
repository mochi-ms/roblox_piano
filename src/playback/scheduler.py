"""
Roblox Piano Player - High-Precision Playback Scheduler
"""
import time
import threading
from enum import Enum
from typing import Optional, List, Dict, Callable
from src.music.events import NoteEvent, ChordGroup
from src.music.timeline import MusicTimeline
from src.playback.chord_engine import ChordEngine
from src.playback.key_state_manager import KeyStateManager
from src.playback.pedal_backend import MousePedalBackend


class PlaybackState(Enum):
    IDLE = "IDLE"
    COUNTDOWN = "COUNTDOWN"
    PLAYING = "PLAYING"
    PAUSED = "PAUSED"
    STOPPED = "STOPPED"
    COMPLETED = "COMPLETED"


class PlaybackScheduler:
    """
    High-precision monotonic Playback Scheduler running on a dedicated Worker Thread.
    Uses time.perf_counter() absolute time targeting for jitter-free playback.
    """

    def __init__(
        self,
        chord_engine: ChordEngine,
        key_state: KeyStateManager
    ):
        self.chord_engine: ChordEngine = chord_engine
        self.key_state: KeyStateManager = key_state

        # Playback settings
        self.speed: float = 1.0
        self.countdown_seconds: int = 3
        self.enable_rh: bool = True
        self.enable_lh: bool = True
        self.track_filter: Optional[Dict[int, bool]] = None

        # Callbacks
        self.on_state_changed: Optional[Callable[[PlaybackState], None]] = None
        self.on_progress: Optional[Callable[[float, float], None]] = None
        self.on_countdown: Optional[Callable[[int], None]] = None
        self.on_chord_played: Optional[Callable[[List[NoteEvent]], None]] = None
        self.on_log: Optional[Callable[[str], None]] = None
        self.target_window_check_fn: Optional[Callable[[], bool]] = None
        self.target_hwnd_getter: Optional[Callable[[], Optional[int]]] = None
        self.pedal_backend: Optional[MousePedalBackend] = None

        # Threading state
        self._state: PlaybackState = PlaybackState.IDLE
        self._thread: Optional[threading.Thread] = None
        self._stop_event = threading.Event()
        self._pause_event = threading.Event()
        self._pause_event.set()  # Set means NOT paused

        self._timeline: Optional[MusicTimeline] = None
        self._current_time: float = 0.0
        self._total_time: float = 0.0
        self._lock = threading.Lock()

    @property
    def state(self) -> PlaybackState:
        return self._state

    @property
    def current_time(self) -> float:
        return self._current_time

    @property
    def total_time(self) -> float:
        return self._total_time

    def set_timeline(self, timeline: MusicTimeline) -> None:
        self.stop()
        self._timeline = timeline
        self._current_time = 0.0
        self._total_time = timeline.duration if timeline else 0.0

    def _set_state(self, new_state: PlaybackState) -> None:
        self._state = new_state
        if self.on_state_changed:
            self.on_state_changed(new_state)

    def _log(self, msg: str) -> None:
        if self.on_log:
            self.on_log(msg)

    def play(self, start_offset: Optional[float] = None) -> None:
        """Starts or restarts playback."""
        if not self._timeline or not self._timeline.notes:
            self._log("Cannot play: No score loaded.")
            return

        if self._state == PlaybackState.PAUSED:
            self.resume()
            return

        self.stop()
        if start_offset is not None:
            self._current_time = max(0.0, min(start_offset, self._total_time))
        else:
            self._current_time = 0.0

        self._stop_event.clear()
        self._pause_event.set()

        self._thread = threading.Thread(target=self._worker_loop, daemon=True)
        self._thread.start()

    def pause(self) -> None:
        if self._state == PlaybackState.PLAYING:
            self._pause_event.clear()
            self._set_state(PlaybackState.PAUSED)
            self.key_state.release_all()
            self._log("Playback paused.")

    def resume(self) -> None:
        if self._state == PlaybackState.PAUSED:
            self._set_state(PlaybackState.PLAYING)
            self._pause_event.set()
            self._log("Playback resumed.")

    def toggle_play_pause(self) -> None:
        if self._state == PlaybackState.PLAYING:
            self.pause()
        elif self._state == PlaybackState.PAUSED:
            self.resume()
        elif self._state in (PlaybackState.IDLE, PlaybackState.STOPPED, PlaybackState.COMPLETED):
            self.play()

    def stop(self) -> None:
        """Immediate emergency or routine stop."""
        self._stop_event.set()
        self._pause_event.set()  # Unblock if paused
        if self._thread and self._thread.is_alive():
            self._thread.join(timeout=0.5)
        self._thread = None
        self.key_state.release_all()
        if self.pedal_backend and self.target_hwnd_getter:
            hwnd = self.target_hwnd_getter()
            if hwnd:
                self.pedal_backend.release_all(hwnd)
        self._set_state(PlaybackState.STOPPED)
        self._log("Playback stopped.")

    def seek(self, target_time: float) -> None:
        was_playing = (self._state == PlaybackState.PLAYING)
        self.stop()
        self._current_time = max(0.0, min(target_time, self._total_time))
        if self.on_progress:
            self.on_progress(self._current_time, self._total_time)
        if was_playing:
            self.play(start_offset=self._current_time)

    def set_speed(self, speed: float) -> None:
        self.speed = max(0.25, min(speed, 3.0))

    def _precise_sleep_until(self, target_perf_time: float) -> bool:
        """
        High-precision hybrid sleep until target_perf_time.
        Returns False if stop_event was triggered.
        """
        while True:
            if self._stop_event.is_set():
                return False

            # Check pause
            if not self._pause_event.is_set():
                self.key_state.release_all()
                self._pause_event.wait()
                if self._stop_event.is_set():
                    return False
                # Re-anchor target timing after unpause handled outside or adjust

            now = time.perf_counter()
            remaining = target_perf_time - now

            if remaining <= 0:
                break
            elif remaining > 0.005:
                # Normal OS sleep for bulk duration
                time.sleep(remaining - 0.003)
            else:
                # Busy wait spinlock for sub-millisecond precision
                while time.perf_counter() < target_perf_time:
                    if self._stop_event.is_set():
                        return False
                break

        return True

    def _worker_loop(self) -> None:
        try:
            # 1. Countdown Phase
            if self.countdown_seconds > 0:
                self._set_state(PlaybackState.COUNTDOWN)
                for sec in range(self.countdown_seconds, 0, -1):
                    if self._stop_event.is_set():
                        return
                    if self.on_countdown:
                        self.on_countdown(sec)
                    self._log(f"Starting in {sec}...")
                    target = time.perf_counter() + 1.0
                    if not self._precise_sleep_until(target):
                        return

            if self._stop_event.is_set():
                return

            self._set_state(PlaybackState.PLAYING)
            self._log("Playback started!")

            # 2. Filter notes and build chord groups
            filtered_notes = self._timeline.get_filtered_notes(
                enable_rh=self.enable_rh,
                enable_lh=self.enable_lh,
                track_filter=self.track_filter
            )

            # Filter by current_time offset
            chord_groups = self._timeline.build_chord_groups(filtered_notes)
            
            # Merge chords and pedals
            events = []
            for cg in chord_groups:
                if cg.start_time >= self._current_time:
                    events.append((cg.start_time, "chord", cg))
            
            for p in self._timeline.pedals:
                if p.time >= self._current_time:
                    events.append((p.time, "pedal", p))
                    
            events.sort(key=lambda x: x[0])

            if not events:
                self._set_state(PlaybackState.COMPLETED)
                return

            start_song_time = self._current_time
            perf_anchor = time.perf_counter()

            for ev_time, ev_type, ev_data in events:
                if self._stop_event.is_set():
                    break

                # Handle pause
                if not self._pause_event.is_set():
                    pause_start_perf = time.perf_counter()
                    self._pause_event.wait()
                    if self._stop_event.is_set():
                        break
                    # Adjust anchor for paused duration
                    paused_duration = time.perf_counter() - pause_start_perf
                    perf_anchor += paused_duration

                # Calculate target perf time
                delta_song_time = (ev_time - start_song_time) / self.speed
                target_perf = perf_anchor + delta_song_time

                if not self._precise_sleep_until(target_perf):
                    break

                # Target window check (auto-pause if focus lost)
                hwnd = None
                if self.target_hwnd_getter:
                    hwnd = self.target_hwnd_getter()
                
                if self.target_window_check_fn:
                    if not self.target_window_check_fn():
                        self.pause()
                        continue

                # Play the event
                self._current_time = ev_time
                if self.on_progress:
                    self.on_progress(self._current_time, self._total_time)

                if ev_type == "chord":
                    cg = ev_data
                    if self.on_chord_played:
                        self.on_chord_played(cg.notes)
                    self.chord_engine.play_chord_notes(cg.notes)
                elif ev_type == "pedal":
                    p = ev_data
                    if self.pedal_backend and hwnd:
                        if p.down:
                            self.pedal_backend.pedal_down(hwnd)
                        else:
                            self.pedal_backend.pedal_up(hwnd)

            if not self._stop_event.is_set():
                self._current_time = self._total_time
                if self.on_progress:
                    self.on_progress(self._current_time, self._total_time)
                self._set_state(PlaybackState.COMPLETED)
                self._log("Playback finished.")

        except Exception as e:
            self._log(f"Playback error: {str(e)}")
            self._set_state(PlaybackState.STOPPED)
        finally:
            self.key_state.release_all()
