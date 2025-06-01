#!/usr/bin/env python3
"""
Elastic Beanstalk用のデプロイメントパッケージを作成するスクリプト
Windowsでも正しいパス区切り文字でZIPファイルを作成します
"""

import os
import zipfile
import shutil
from pathlib import Path

def create_deployment_package():
    """デプロイメントパッケージを作成"""
    
    # 出力ファイル名
    output_file = 'eb-deploy-final.zip'
    
    # 含めるファイルとディレクトリ
    files_to_include = [
        'application.py',
        'wsgi.py',
        'requirements.txt',
        'config.py',
        'models.py',
        'migrations.py',
        'static_qa_data.py',
        'Procfile',
        '.env_new',
        'get_coefont_list.py',
        'get_coefont_list_standalone.py'
    ]
    
    dirs_to_include = [
        'modules',
        'static',
        'templates',
        '.ebextensions',
        '.platform',
        'data',
        'uploads',
        'Assets',
        'instance'
    ]
    
    # 既存のZIPファイルを削除
    if os.path.exists(output_file):
        os.remove(output_file)
        print(f"Removed existing {output_file}")
    
    # ZIPファイルを作成（Linuxパス形式を強制）
    with zipfile.ZipFile(output_file, 'w', zipfile.ZIP_DEFLATED) as zipf:
        # ファイルを追加
        for file in files_to_include:
            if os.path.exists(file):
                # スラッシュを使用してパスを正規化
                arcname = file.replace('\\', '/')
                zipf.write(file, arcname)
                print(f"Added file: {arcname}")
            else:
                print(f"Warning: File not found: {file}")
        
        # ディレクトリを追加
        for dir_name in dirs_to_include:
            if os.path.exists(dir_name):
                for root, dirs, files in os.walk(dir_name):
                    # __pycache__ディレクトリをスキップ
                    if '__pycache__' in root:
                        continue
                    
                    for file in files:
                        # .pycファイルをスキップ
                        if file.endswith('.pyc'):
                            continue
                        
                        # modulesディレクトリの.pyファイルは確実に含める
                        if dir_name == 'modules' and not file.endswith('.py'):
                            continue
                            
                        file_path = os.path.join(root, file)
                        # Windowsパスをスラッシュに変換
                        arcname = file_path.replace('\\', '/')
                        try:
                            zipf.write(file_path, arcname)
                            print(f"Added: {arcname}")
                        except Exception as e:
                            print(f"Error adding {file_path}: {e}")
            else:
                print(f"Warning: Directory not found: {dir_name}")
    
    # ZIPファイルの内容を確認
    print("\n=== ZIP File Contents ===")
    with zipfile.ZipFile(output_file, 'r') as zipf:
        file_list = zipf.namelist()
        print(f"Total files: {len(file_list)}")
        
        # 重要なファイルの存在確認
        important_files = ['requirements.txt', 'config.py', 'application.py', 'wsgi.py', 'static_qa_data.py']
        for file in important_files:
            if file in file_list:
                print(f"✓ {file} is included")
            else:
                print(f"✗ {file} is MISSING!")
    
    print(f"\nDeployment package created: {output_file}")
    print(f"File size: {os.path.getsize(output_file) / 1024 / 1024:.2f} MB")

if __name__ == "__main__":
    create_deployment_package() 