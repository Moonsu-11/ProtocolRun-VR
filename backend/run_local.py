"""Cross-platform local launcher. Generates a private token and opens the local console."""
from pathlib import Path
import os
import secrets
import threading
import webbrowser

root = Path(__file__).resolve().parent
os.chdir(root)
config = root / '.env.local'
if not config.exists():
    # Owner-only mode on POSIX. Windows uses the user's directory ACL.
    fd = os.open(config, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, 'w') as f:
        f.write('PRVR_ADMIN_TOKEN=' + secrets.token_urlsafe(48) + '\nPRVR_STORE=sqlite\nPRVR_SQLITE_PATH=protocolrun.sqlite3\n'
                'PRVR_CORS_ORIGINS=http://localhost:5173\nGOOGLE_GENAI_USE_VERTEXAI=FALSE\n'
                'GEMINI_MODEL=gemini-3.5-flash\n# For login-free local Gemini, set GOOGLE_API_KEY below. Never commit this file.\n'
                '# GOOGLE_API_KEY=\n# For Vertex AI instead, set GOOGLE_GENAI_USE_VERTEXAI=TRUE, GOOGLE_CLOUD_PROJECT and GOOGLE_CLOUD_LOCATION=global.\n')
from dotenv import load_dotenv
load_dotenv(config)
print('Open http://127.0.0.1:8080/console/ . Read the researcher token privately from backend/.env.local.')
print('Real Gemini requires GOOGLE_API_KEY or authenticated Vertex AI. Missing credentials never enable simulated recovery.')
import uvicorn
threading.Timer(1.0, lambda: webbrowser.open('http://127.0.0.1:8080/console/')).start()
uvicorn.run('protocolrun.api:create_app', factory=True, host='127.0.0.1', port=8080)
