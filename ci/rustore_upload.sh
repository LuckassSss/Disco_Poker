#!/usr/bin/env bash
# Загрузка AAB в RuStore: токен → черновик версии → файл → отправка на модерацию.
# https://www.rustore.ru/help/work-with-rustore-api/api-upload-publication-app
# Работает, только если у приложения уже есть активная версия (первую публикуют руками в консоли).
#
#   RUSTORE_KEY_ID=… RUSTORE_PRIVATE_KEY=<ключ из консоли, base64> ci/rustore_upload.sh <package> <file.aab>
set -euo pipefail

PACKAGE="$1"
FILE="$2"
API=https://public-api-m.rustore.ru

# ключ из консоли — PKCS#8 в base64, openssl подписывает PEM
KEY="$(mktemp)"
trap 'rm -f "$KEY"' EXIT
{ echo "-----BEGIN PRIVATE KEY-----"; printf '%s' "$RUSTORE_PRIVATE_KEY" | tr -d '\n ' | fold -w 64; echo; echo "-----END PRIVATE KEY-----"; } > "$KEY"

# подпись SHA512withRSA от keyId+timestamp, живёт минуту
TS="$(python3 -c "import datetime; print(datetime.datetime.now(datetime.timezone.utc).isoformat(timespec='milliseconds'))")"
SIG="$(printf '%s%s' "$RUSTORE_KEY_ID" "$TS" | openssl dgst -sha512 -sign "$KEY" | base64 | tr -d '\n')"

AUTH="$(curl -sS -X POST "$API/public/auth/" -H 'Content-Type: application/json' \
  -d "{\"keyId\":\"$RUSTORE_KEY_ID\",\"timestamp\":\"$TS\",\"signature\":\"$SIG\"}")"
TOKEN="$(printf '%s' "$AUTH" | python3 -c 'import json,sys; print(json.load(sys.stdin)["body"]["jwe"])')" \
  || { echo "RuStore auth: $AUTH" >&2; exit 1; }

# черновик: новый id в body; если черновик уже есть — его id в тексте ошибки
DRAFT_RESP="$(curl -sS -X POST "$API/public/v1/application/$PACKAGE/version" \
  -H "Public-Token: $TOKEN" -H 'Content-Type: application/json' -d '{}')"
DRAFT="$(printf '%s' "$DRAFT_RESP" | python3 -c 'import json,re,sys; d=json.load(sys.stdin); print(d.get("body") or re.search(r"\d+", d.get("message") or "").group())')" \
  || { echo "RuStore draft: $DRAFT_RESP" >&2; exit 1; }
echo "RuStore: черновик $DRAFT"

# ответ RuStore с code != OK — вывести его и упасть
ok() {
  python3 -c '
import json, sys
r = sys.stdin.read()
if json.loads(r).get("code") != "OK":
    sys.exit(r)
'
}

curl -sS -X POST "$API/public/v1/application/$PACKAGE/version/$DRAFT/aab" \
  -H "Public-Token: $TOKEN" -F "file=@$FILE" | ok
curl -sS -X POST "$API/public/v1/application/$PACKAGE/version/$DRAFT/commit" \
  -H "Public-Token: $TOKEN" | ok
echo "RuStore: версия $DRAFT отправлена на модерацию"
