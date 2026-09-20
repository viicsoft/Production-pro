import json

log_path = r'C:\Users\USER\.gemini\antigravity\brain\a420663b-f942-4219-8d2e-f851d4c8b9db\.system_generated\logs\transcript_full.jsonl'
target = 'AtemHardwareAdapter.cs'
out_path = r'C:\Production-pro-main\Production-pro-main\AtemHardwareAdapter_History.txt'

with open(log_path, 'r', encoding='utf-8') as f, open(out_path, 'w', encoding='utf-8') as out:
    for line in f:
        try:
            step = json.loads(line)
        except:
            continue
            
        if 'tool_calls' in step:
            for tc in step['tool_calls']:
                args_str = str(tc.get('args', ''))
                if target in args_str:
                    out.write(f"=== STEP {step.get('step_index')} [{tc['name']}] ===\n")
                    out.write(json.dumps(tc['args'], indent=2))
                    out.write('\n\n')
