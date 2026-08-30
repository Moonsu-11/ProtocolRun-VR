"""Read-only deployed-server check. Never prints credentials; does not assert Gemini/VR success."""
import json
import sys
from pathlib import Path
from urllib.parse import urlparse
from urllib.request import Request, urlopen


def verify(url, token):
    parsed = urlparse(url)
    if parsed.scheme != 'https' and not (parsed.scheme == 'http' and parsed.hostname in ['127.0.0.1', 'localhost']):
        raise ValueError('HTTPS required outside loopback')
    if parsed.username or parsed.password or parsed.query or parsed.fragment:
        raise ValueError('Use only the server origin')
    def get(path, authenticated=True):
        req = Request(url.rstrip('/') + path, headers={'Authorization': 'Bearer ' + token} if authenticated else {})
        with urlopen(req, timeout=20) as response:
            return response.read().decode()
    health = json.loads(get('/healthz', False))
    runtime = json.loads(get('/api/runtime'))
    protocols = json.loads(get('/api/protocols'))['protocols']
    assert health['version'] == '0.5.0-rc6', 'Unexpected server version'
    assert any(p['id'] == 'meta-hands-v1' and p['adapter'] == 'meta_hands' for p in protocols)
    assert '<html lang="en">' in get('/console/', False)
    print(json.dumps({'server': url, 'version': health['version'], 'store': runtime['store'], 'cloud_run': runtime['cloud_run'],
                      'read_only_smoke': 'passed', 'live_gemini_and_quest': 'NOT VERIFIED'}))


if __name__ == '__main__':
    if len(sys.argv) != 2:
        raise SystemExit('Usage: python deploy/verify_server.py backend/.env.connection')
    values = dict(line.split('=', 1) for line in Path(sys.argv[1]).read_text().splitlines() if '=' in line and not line.startswith('#'))
    verify(values['PRVR_SERVER_URL'], values['PRVR_ADMIN_TOKEN'])
