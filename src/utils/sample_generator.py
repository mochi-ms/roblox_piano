"""
Script to generate sample MIDI songs for testing Roblox Piano Player
"""
import os
import mido

SAMPLES_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "samples")
os.makedirs(SAMPLES_DIR, exist_ok=True)

def generate_fur_elise():
    mid = mido.MidiFile(ticks_per_beat=480)
    
    # Track 1: Right Hand Melody
    t_rh = mido.MidiTrack()
    t_rh.append(mido.MetaMessage('track_name', name='Right Hand', time=0))
    t_rh.append(mido.MetaMessage('set_tempo', tempo=mido.bpm2tempo(130), time=0))
    
    # Notes for Fur Elise opening: E5(76), D#5(75), E5(76), D#5(75), E5(76), B4(71), D5(74), C5(72), A4(69)
    melody = [
        (76, 240), (75, 240), (76, 240), (75, 240), (76, 240),
        (71, 240), (74, 240), (72, 240), (69, 480)
    ]
    for pitch, dur in melody:
        t_rh.append(mido.Message('note_on', note=pitch, velocity=80, time=0))
        t_rh.append(mido.Message('note_off', note=pitch, velocity=0, time=dur))

    # Track 2: Left Hand Arpeggio
    t_lh = mido.MidiTrack()
    t_lh.append(mido.MetaMessage('track_name', name='Left Hand', time=0))
    # Rest for first 5 sixteenth notes (1200 ticks), then A2(45), E3(52), A3(57)
    t_lh.append(mido.Message('note_on', note=45, velocity=70, time=1200))
    t_lh.append(mido.Message('note_off', note=45, velocity=0, time=240))
    t_lh.append(mido.Message('note_on', note=52, velocity=70, time=0))
    t_lh.append(mido.Message('note_off', note=52, velocity=0, time=240))
    t_lh.append(mido.Message('note_on', note=57, velocity=70, time=0))
    t_lh.append(mido.Message('note_off', note=57, velocity=0, time=480))

    mid.tracks.append(t_rh)
    mid.tracks.append(t_lh)
    
    out_path = os.path.join(SAMPLES_DIR, "Fur_Elise.mid")
    mid.save(out_path)
    print(f"Generated sample: {out_path}")

if __name__ == "__main__":
    generate_fur_elise()
