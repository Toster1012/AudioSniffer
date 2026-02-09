class SilenceConfig:
    top_db = 28.0
    min_silence_duration = 0.4
    max_natural_silence = 3.5
    extreme_silence = 6.0
    absolute_silence_rms = 0.0001
    transition_energy = 0.45


class PitchConfig:
    frame_length = 2048
    hop_length = 512
    anomaly_threshold = 3.8
    min_pitch = 50
    max_pitch = 550
    smoothing_window = 7
    min_group_size = 4
    pitch_change_threshold = 60


class SpliceConfig:
    n_fft = 2048
    hop_length = 512
    discontinuity_threshold = 0.82
    phase_threshold = 0.72
    min_distance_sec = 0.4
    min_confidence = 0.68