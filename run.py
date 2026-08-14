"""
Roblox Piano Player - Quick Run Launcher
"""
import sys
import os

if __name__ == "__main__":
    src_dir = os.path.dirname(os.path.abspath(__file__))
    if src_dir not in sys.path:
        sys.path.insert(0, src_dir)

    from src.app.main import main
    main()
