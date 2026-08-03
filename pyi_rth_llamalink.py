"""Runtime settings for the frozen Qt application."""

import os


# Keep high-DPI behavior deterministic in packaged Windows launches.
os.environ.setdefault("QT_ENABLE_HIGHDPI_SCALING", "1")
os.environ.setdefault("QT_AUTO_SCREEN_SCALE_FACTOR", "1")
