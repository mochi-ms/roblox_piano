import os
import json
import uuid
import shutil
import re
import argparse
import csv
from pathlib import Path

# Add project root to sys.path
import sys
project_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.append(project_root)

from src.utils.config import ConfigManager
from src.library.database import ScoreDatabase
from src.library.models import FolderItem
from src.library.manager import LibraryManager

def categorize_song(title: str, filepath: str) -> str:
    """Very basic categorization based on filename/title heuristics."""
    title_lower = title.lower()
    
    if any(kw in title_lower for kw in ['mozart', 'chopin', 'beethoven', 'bach', 'liszt', '클래식', 'classic', 'nocturne', 'sonata', '행진곡', '변주곡']):
        return "클래식"
    if any(kw in title_lower for kw in ['ost', 'anime', '애니', '주제', 'zen zen', 'radwimps', 'ghibli', '지브리', 'joe hisaishi']):
        return "애니메이션· OST"
    if any(kw in title_lower for kw in ['j-pop', 'jpop', '제이팝', '요네즈켄시', 'yoasobi', 'kenshi', 'aimyon']):
        return "J-POP"
    if any(kw in title_lower for kw in ['bgm', 'game', '게임', 'mario', 'zelda', 'maplestory']):
        return "게임 · BGM"
    if any(kw in title_lower for kw in ['k-pop', 'kpop', 'bts', '아이유', 'iu', '한국']):
        return "한국팝"
    
    return "미분류"

def get_safe_filename(library_dir: str, desired_name: str) -> str:
    desired_name = re.sub(r'[\\/*?:"<>|]', "", desired_name)
    base, ext = os.path.splitext(desired_name)
    candidate = desired_name
    counter = 1
    while os.path.exists(os.path.join(library_dir, candidate)):
        candidate = f"{base} ({counter}){ext}"
        counter += 1
    return candidate

def main():
    parser = argparse.ArgumentParser(description="Migrate Roblox Piano Player library.")
    parser.add_argument("--dry-run", action="store_true", help="Preview changes without applying them.")
    parser.add_argument("--apply", action="store_true", help="Actually apply the changes to the library.")
    args = parser.parse_args()
    
    if not args.dry_run and not args.apply:
        print("Please specify either --dry-run or --apply")
        return

    config = ConfigManager(project_root).config
    library_dir = config.library_dir
    os.makedirs(library_dir, exist_ok=True)
    
    db_path = os.path.join(library_dir, "library.db")
    
    if not os.path.exists(db_path):
        print(f"No database found at {db_path}. Using empty DB.")
        db = ScoreDatabase(db_path)
    else:
        if args.apply:
            backup_path = db_path + f".backup_migrate"
            shutil.copy2(db_path, backup_path)
            print(f"Backed up DB to {backup_path}")
        db = ScoreDatabase(db_path)
        
    report = []
    
    # 1. Migrate UUID files to Original Filenames
    print("\n--- Migrating UUID files ---")
    scores = db.get_all_scores()
    for item in scores:
        if os.path.exists(item.filepath):
            basename = os.path.basename(item.filepath)
            if len(basename) >= 36 and '-' in basename:
                ext = item.file_extension if item.file_extension else os.path.splitext(basename)[1]
                title = item.original_filename
                if not title:
                    title = item.title if item.title else "Untitled"
                
                desired_name = f"{os.path.splitext(title)[0]}{ext}"
                safe_name = get_safe_filename(library_dir, desired_name)
                new_filepath = os.path.join(library_dir, safe_name)
                
                old_path = item.filepath
                
                report.append({
                    "type": "uuid_migration",
                    "old_path": old_path,
                    "new_path": new_filepath,
                    "category": ""
                })
                
                print(f"[UUID] Renaming: {basename} -> {safe_name}")
                if args.apply:
                    try:
                        os.rename(old_path, new_filepath)
                        item.filepath = new_filepath
                        if not item.original_filename:
                            item.original_filename = safe_name
                        db.update_score(item)
                    except Exception as e:
                        print(f"Failed to rename {basename}: {e}")
                    
    # 2. Gather user scores from Desktop and move to Library
    print("\n--- Gathering user scores from Desktop ---")
    desktop_dir = project_root
    
    # Stricter filtering
    allowed_extensions = {'.mid', '.midi', '.mxl', '.musicxml', '.xml', '.mml'}
    
    category_folders = {}
    
    def get_or_create_folder(cat_name: str) -> str:
        if cat_name in category_folders:
            return category_folders[cat_name]
        
        folders = db.get_all_folders()
        for f in folders:
            if f.name == cat_name:
                category_folders[cat_name] = f.id
                return f.id
                
        fid = str(uuid.uuid4())
        fitem = FolderItem(id=fid, parent_id=None, name=cat_name)
        if args.apply:
            db.insert_folder(fitem)
            # Make physical dir
            os.makedirs(os.path.join(library_dir, cat_name), exist_ok=True)
            
        category_folders[cat_name] = fid
        return fid

    for root, dirs, files in os.walk(desktop_dir):
        if any(exc in root for exc in ['src', 'tests', 'build', 'dist', '.git', '.venv', '__pycache__', 'tools', 'Docs', 'logs']):
            continue
            
        for f in files:
            ext = os.path.splitext(f)[1].lower()
            if ext in allowed_extensions:
                if f in ['RobloxPianoPlayer.spec']:
                    continue
                    
                old_path = os.path.join(root, f)
                if old_path.startswith(library_dir):
                    continue
                    
                cat = categorize_song(f, old_path)
                fid = get_or_create_folder(cat)
                
                target_dir = os.path.join(library_dir, cat)
                safe_name = get_safe_filename(target_dir, f)
                new_filepath = os.path.join(target_dir, safe_name)
                
                report.append({
                    "type": "file_move",
                    "old_path": old_path,
                    "new_path": new_filepath,
                    "category": cat
                })
                
                print(f"[MOVE] {f} -> {cat}/{safe_name}")
                if args.apply:
                    try:
                        shutil.move(old_path, new_filepath)
                        from src.library.models import ScoreItem
                        import time
                        item = ScoreItem(
                            id=str(uuid.uuid4()),
                            title=os.path.splitext(f)[0],
                            source_type="FILE",
                            source_url=old_path,
                            filepath=new_filepath,
                            original_filename=f,
                            file_extension=ext,
                            folder_id=fid,
                            created_at=time.time()
                        )
                        db.insert_score(item)
                    except Exception as e:
                        print(f"Failed to move {f}: {e}")

    report_json_path = os.path.join(project_root, "library_rename_report.json")
    report_csv_path = os.path.join(project_root, "library_rename_report.csv")
    
    with open(report_json_path, "w", encoding="utf-8") as rf:
        json.dump(report, rf, indent=2, ensure_ascii=False)
        
    with open(report_csv_path, "w", encoding="utf-8", newline='') as cf:
        writer = csv.DictWriter(cf, fieldnames=["type", "old_path", "new_path", "category"])
        writer.writeheader()
        for r in report:
            writer.writerow(r)
            
    if args.dry_run:
        print(f"\nDRY RUN complete. Report saved to {report_json_path} and {report_csv_path}")
    else:
        print(f"\nMigration complete. Report saved to {report_json_path} and {report_csv_path}")

if __name__ == "__main__":
    main()
