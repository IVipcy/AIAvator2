import zipfile

with zipfile.ZipFile('eb-deploy-final.zip', 'r') as z:
    all_files = z.namelist()
    
    print("=== Module Files ===")
    module_files = [f for f in all_files if f.startswith('modules/') and f.endswith('.py')]
    for f in module_files:
        print(f)
    
    print("\n=== Missing Module Files ===")
    expected_modules = ['modules/rag_system.py', 'modules/speech_processor.py', 
                       'modules/openai_tts_client.py', 'modules/coe_font_client.py', 
                       'modules/emotion_voice_params.py']
    for module in expected_modules:
        if module not in all_files:
            print(f"MISSING: {module}")
        else:
            print(f"OK: {module}") 