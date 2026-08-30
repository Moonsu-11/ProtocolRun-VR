#!/usr/bin/env bash
# Run in YOUR authenticated Google Cloud Shell. Never run with shell tracing (set -x).
set -euo pipefail
umask 077
if [ "$#" -lt 1 ]; then
  echo 'Usage: bash deploy/deploy_gcp.sh ACTUAL_PROJECT_ID [https://EXTERNAL_DASHBOARD_ORIGIN|-] [asia-northeast3]'
  exit 2
fi
PRVR_PROJECT="$1"
PRVR_ORIGIN="${2:--}"
PRVR_REGION="${3:-asia-northeast3}"
PRVR_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PRVR_RUNTIME_SA="protocolrun-api@${PRVR_PROJECT}.iam.gserviceaccount.com"
PRVR_BUILD_SA="protocolrun-build@${PRVR_PROJECT}.iam.gserviceaccount.com"
PRVR_SECRET="protocolrun-admin-token"
PRVR_TMP="$(mktemp -d)"
trap 'rm -rf "$PRVR_TMP"' EXIT
python3 - "$PRVR_PROJECT" "$PRVR_ORIGIN" "$PRVR_REGION" <<'PY'
import re, sys
from urllib.parse import urlparse
project, origin, region = sys.argv[1:]
u = urlparse(origin)
assert re.fullmatch(r'[a-z][a-z0-9-]{4,28}[a-z0-9]', project), 'Use the actual project ID, not display name.'
assert origin == '-' or (u.scheme == 'https' and u.netloc and u.path in ['', '/'] and not u.username and not u.password and not u.query and not u.fragment), 'Use an HTTPS dashboard origin, or - for the bundled console.'
assert re.fullmatch(r'[a-z]+-[a-z]+[0-9]+', region), 'Invalid region.'
PY
echo "Target project: $PRVR_PROJECT / region: $PRVR_REGION / service: protocolrun-vr"
echo 'This creates Google Cloud resources and IAM bindings and can incur charges. The HTTP endpoint uses application-level tokens.'
read -r -p 'Type DEPLOY to continue: ' PRVR_CONFIRM
[ "$PRVR_CONFIRM" = 'DEPLOY' ] || exit 1

gcloud services enable run.googleapis.com cloudbuild.googleapis.com artifactregistry.googleapis.com \
  aiplatform.googleapis.com firestore.googleapis.com secretmanager.googleapis.com \
  iam.googleapis.com logging.googleapis.com --project="$PRVR_PROJECT"

gcloud iam service-accounts list --project="$PRVR_PROJECT" --format='value(email)' > "$PRVR_TMP/accounts"
if ! grep -Fxq "$PRVR_RUNTIME_SA" "$PRVR_TMP/accounts"; then
  gcloud iam service-accounts create protocolrun-api --display-name='ProtocolRun VR runtime' --project="$PRVR_PROJECT"
fi
if ! grep -Fxq "$PRVR_BUILD_SA" "$PRVR_TMP/accounts"; then
  gcloud iam service-accounts create protocolrun-build --display-name='ProtocolRun VR source build' --project="$PRVR_PROJECT"
fi
for PRVR_ROLE in roles/aiplatform.user roles/datastore.user roles/logging.logWriter; do
  gcloud projects add-iam-policy-binding "$PRVR_PROJECT" --member="serviceAccount:$PRVR_RUNTIME_SA" --role="$PRVR_ROLE" --condition=None --quiet >/dev/null
done
gcloud projects add-iam-policy-binding "$PRVR_PROJECT" --member="serviceAccount:$PRVR_BUILD_SA" --role=roles/run.builder --condition=None --quiet >/dev/null

gcloud firestore databases list --project="$PRVR_PROJECT" --format=json > "$PRVR_TMP/databases.json"
PRVR_HAS_DB="$(python3 - "$PRVR_TMP/databases.json" <<'PY'
import json, sys
print('yes' if any(x['name'].endswith('/databases/(default)') for x in json.load(open(sys.argv[1]))) else 'no')
PY
)"
if [ "$PRVR_HAS_DB" = 'no' ]; then
  gcloud firestore databases create --database='(default)' --location="$PRVR_REGION" --type=firestore-native --project="$PRVR_PROJECT"
fi

gcloud secrets list --project="$PRVR_PROJECT" --format='value(name)' > "$PRVR_TMP/secrets"
if ! grep -Eq "(^|/)${PRVR_SECRET}$" "$PRVR_TMP/secrets"; then
  gcloud secrets create "$PRVR_SECRET" --replication-policy=automatic --project="$PRVR_PROJECT"
  python3 -c 'import secrets; print(secrets.token_urlsafe(48), end="")' > "$PRVR_TMP/token"
  gcloud secrets versions add "$PRVR_SECRET" --data-file="$PRVR_TMP/token" --project="$PRVR_PROJECT" >/dev/null
fi
gcloud secrets add-iam-policy-binding "$PRVR_SECRET" --member="serviceAccount:$PRVR_RUNTIME_SA" --role=roles/secretmanager.secretAccessor --project="$PRVR_PROJECT" >/dev/null

python3 - "$PRVR_TMP/env.json" "$PRVR_PROJECT" "${PRVR_ORIGIN%/}" <<'PY'
import json, sys
path, project, origin = sys.argv[1:]
if origin == '-': origin = ''
json.dump({'PRVR_STORE':'firestore','GOOGLE_CLOUD_PROJECT':project,'GOOGLE_CLOUD_LOCATION':'global',
           'GOOGLE_GENAI_USE_VERTEXAI':'TRUE','GEMINI_MODEL':'gemini-3.5-flash',
           'PRVR_CORS_ORIGINS':origin}, open(path,'w'))
PY
gcloud run deploy protocolrun-vr --source="$PRVR_ROOT/backend" --project="$PRVR_PROJECT" --region="$PRVR_REGION" \
  --build-service-account="projects/$PRVR_PROJECT/serviceAccounts/$PRVR_BUILD_SA" \
  --service-account="$PRVR_RUNTIME_SA" --env-vars-file="$PRVR_TMP/env.json" \
  --set-secrets="PRVR_ADMIN_TOKEN=$PRVR_SECRET:latest" \
  --allow-unauthenticated --memory=1Gi --cpu=1 --min=0 --max=1 --concurrency=20 --timeout=120 --quiet

PRVR_SERVICE_URL="$(gcloud run services describe protocolrun-vr --project="$PRVR_PROJECT" --region="$PRVR_REGION" --format='value(status.url)')"
gcloud secrets versions access latest --secret="$PRVR_SECRET" --project="$PRVR_PROJECT" > "$PRVR_TMP/token"
python3 - "$PRVR_ROOT/backend/.env.connection" "$PRVR_SERVICE_URL" "$PRVR_TMP/token" <<'PY'
import pathlib, sys
path, url, token_path = sys.argv[1:]
pathlib.Path(path).write_text('PRVR_SERVER_URL='+url+'\nPRVR_ADMIN_TOKEN='+pathlib.Path(token_path).read_text().strip()+'\n')
PY
curl --fail --silent --show-error "$PRVR_SERVICE_URL/healthz"
printf '\nAPI: %s\n' "$PRVR_SERVICE_URL"
printf 'Standalone console: %s/console/\n' "$PRVR_SERVICE_URL"
echo 'Researcher token saved to backend/.env.connection. Open privately; do not upload, screenshot, commit or paste into chat.'
echo 'Health success does NOT verify Gemini, Firestore transactions or VR. Complete the acceptance checklist in docs/START_HERE_KO.md.'
