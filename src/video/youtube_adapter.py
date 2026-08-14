import os
import yt_dlp
from typing import Optional

class YoutubeAdapter:
    """
    Downloads YouTube videos or fetches metadata using yt-dlp.
    """
    def __init__(self, download_dir: str):
        self.download_dir = download_dir
        os.makedirs(self.download_dir, exist_ok=True)

    def fetch_metadata(self, url: str) -> Optional[dict]:
        """
        Fetches title and duration without downloading.
        """
        ydl_opts = {
            'quiet': True,
            'no_warnings': True,
            'extract_flat': True,
        }
        try:
            with yt_dlp.YoutubeDL(ydl_opts) as ydl:
                info = ydl.extract_info(url, download=False)
                if not info:
                    return None
                return {
                    'title': info.get('title', 'Unknown Title'),
                    'duration': info.get('duration', 0.0),
                    'id': info.get('id', 'unknown')
                }
        except Exception as e:
            print(f"Error fetching metadata for {url}: {e}")
            return None

    def download_video(self, url: str, progress_hook=None) -> Optional[str]:
        """
        Downloads the video in the best quality suitable for frame extraction (up to 1080p).
        Returns the path to the downloaded file.
        """
        ydl_opts = {
            'format': 'bestvideo[height<=1080][ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best',
            'outtmpl': os.path.join(self.download_dir, '%(id)s.%(ext)s'),
            'quiet': True,
            'no_warnings': True,
        }
        if progress_hook:
            ydl_opts['progress_hooks'] = [progress_hook]

        try:
            with yt_dlp.YoutubeDL(ydl_opts) as ydl:
                info = ydl.extract_info(url, download=True)
                if not info:
                    return None
                
                # yt-dlp sometimes merges into mkv or webm, we need to find the actual filename
                expected_filename = ydl.prepare_filename(info)
                
                # if merged, extension might change, let's just search the directory for the id
                base_id = info.get('id')
                for f in os.listdir(self.download_dir):
                    if f.startswith(base_id):
                        return os.path.join(self.download_dir, f)
                
                return expected_filename
        except Exception as e:
            print(f"Error downloading {url}: {e}")
            return None
