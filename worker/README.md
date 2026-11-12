# Constellation 중계 서버 (Cloudflare Worker)

Quest 앱과 Claude API 사이에 놓이는 작은 서버입니다. 앱은 별자리 이미지와 키워드만 보내고, API 키는 이 Worker의 시크릿에만 둡니다. 규격은 [기능 명세서](../docs/spec.md#ai-요청응답-규격)를 따릅니다.

## 하는 일

`POST /story` (또는 `POST /`)

1. 공유 토큰 헤더(`X-App-Token`) 확인. 틀리면 401
2. 요청 검증. 실패하면 400, 이미지가 300KB를 넘으면 413
3. 기기당 분당 호출 제한(`X-Device-Id` 기준, 없으면 접속 IP). 넘으면 429
4. Claude API 호출(기본 `claude-haiku-5-5`)
5. 답을 JSON으로 읽고 이름·이야기 규칙을 확인. 통과하면 200, 아니면 502

요청

```json
{ "keyword": "wish", "starCount": 5, "image": "<base64 JPEG>" }
```

응답

```json
{ "name": "소원을 듣는 별", "story": "옛날 ..." }
```

| 상태 | 뜻 |
| --- | --- |
| 200 | 정상. `name`, `story` |
| 400 | 키워드·별 개수·이미지·JSON 형식 오류 |
| 401 | 공유 토큰 없음 또는 불일치 |
| 413 | 이미지 300KB 초과 |
| 429 | 기기당 호출 제한 초과 |
| 500 | 서버에 시크릿이 등록되지 않음 |
| 503 | 카드 저장소(KV)가 연결되지 않음(`/card`만) |
| 502 | Claude 호출 실패, 또는 답이 규칙에 맞지 않음 |

앱은 200이 아니면 모두 폴백 이야기로 넘어갑니다.

### 결과 카드 (선택)

`POST /card` 는 이름·이야기·별자리 이미지를 Cloudflare KV에 저장하고 카드 주소를 돌려줍니다. 앱은 이 주소를 QR로 보여 줍니다. `GET /card/<id>` 는 토큰 없이 누구나 열 수 있는 카드 페이지입니다(주소를 아는 사람만).

```json
{ "keyword": "wish", "name": "소원나무별", "story": "깊은 산골 ...", "image": "<base64 JPEG>" }
```

```json
{ "id": "abcdefghijkm", "url": "https://<Worker 주소>/card/abcdefghijkm" }
```

KV를 연결하지 않으면 `POST /card` 는 503을 돌려주고, 앱은 QR 없이 체험을 끝냅니다. 카드는 30일 뒤 자동으로 지워집니다(`CARD_TTL_DAYS`).

### 모두의 하늘 (선택)

카드를 올릴 때 별자리 모양(`points`, 0~1 범위의 `[x, y]` 2~16개)을 함께 보내면 목록에 쌓이고 번호가 매겨집니다. 응답과 카드 페이지에 "몇 번째 별자리"가 들어갑니다.

```json
{ "id": "abcdefghijkm", "url": "https://<Worker 주소>/card/abcdefghijkm", "number": 12 }
```

| 주소 | 내용 |
| --- | --- |
| `GET /sky` | 지금까지의 개수와 최근 80개의 이름·키워드·모양(JSON). 앱이 먼 하늘에 그릴 때 씁니다. 토큰 없이 읽을 수 있습니다 |
| `GET /wall` | 전시장 모니터에 띄우는 페이지. 최근 24개를 보여 주고 15초마다 새로 고칩니다 |

- 카드와 같은 KV에 저장합니다. KV가 없으면 `/sky`는 503을 돌려주고 카드에는 번호가 붙지 않습니다.
- 번호는 KV 한 칸을 읽고 쓰는 방식이라, 여러 기기가 같은 순간에 올리면 겹칠 수 있습니다. 기기 한 대로 운영하면 문제가 없습니다.
- 한 번 체험할 때 KV에 두 번 씁니다(카드, 목록). 무료 플랜의 하루 1,000번 쓰기 한도로는 하루 약 500명까지입니다.
- 목록을 비우려면 KV에서 `sky:index` 키를 지웁니다.

```bash
npx wrangler kv key delete --binding CARDS "sky:index"
```

## 준비물

- Node.js 20 이상
- Cloudflare 계정(무료 플랜으로 충분)
- Anthropic API 키([콘솔](https://console.anthropic.com/)에서 발급, 월 지출 한도도 여기서 설정)

```bash
cd worker
```

```bash
npm install
```

## 시크릿

키와 토큰은 코드, `wrangler.toml`, 저장소 어디에도 적지 않습니다.

| 이름 | 내용 |
| --- | --- |
| `ANTHROPIC_API_KEY` | Anthropic API 키 |
| `APP_SHARED_TOKEN` | 앱과 공유하는 임의의 긴 문자열. 직접 정한다 |

토큰으로 쓸 임의 문자열은 이렇게 만들 수 있습니다.

```bash
openssl rand -hex 32
```

## 로컬 실행

예시 파일을 복사해 값을 채웁니다. `.dev.vars`는 `.gitignore`에 들어 있어 커밋되지 않습니다.

```bash
cp .dev.vars.example .dev.vars
```

```bash
npm run dev
```

다른 터미널에서 확인합니다. `sample.jpg`는 아무 작은 JPEG면 됩니다.

```bash
curl -s http://localhost:8787/story \
  -H "Content-Type: application/json" \
  -H "X-App-Token: $APP_SHARED_TOKEN" \
  -H "X-Device-Id: local-test" \
  -d "{\"keyword\":\"wish\",\"starCount\":5,\"image\":\"$(base64 -w0 sample.jpg)\"}"
```

macOS에서는 `base64 -w0 sample.jpg` 대신 `base64 -i sample.jpg`를 씁니다.

## 테스트

Claude API 호출은 모킹하므로 키 없이 실행됩니다.

```bash
npm test
```

## 배포

처음 한 번 Cloudflare에 로그인합니다.

```bash
npx wrangler login
```

시크릿을 등록합니다. 명령을 실행하면 값을 묻습니다.

```bash
npx wrangler secret put ANTHROPIC_API_KEY
```

```bash
npx wrangler secret put APP_SHARED_TOKEN
```

결과 카드(QR)를 쓰려면 KV 네임스페이스를 만듭니다. 출력된 `id`를 `wrangler.toml`의 `[[kv_namespaces]]` 주석 블록에 넣고 주석을 풉니다. 카드가 필요 없으면 건너뜁니다.

```bash
npx wrangler kv namespace create CARDS
```

배포합니다. 끝나면 `https://constellation-relay.<계정>.workers.dev` 주소가 나옵니다.

```bash
npm run deploy
```

이 주소와 `APP_SHARED_TOKEN` 값을 Unity 쪽 비밀 설정 파일에 넣습니다(저장소 루트 README의 안내 참고).

## 설정값

비밀이 아닌 값은 `wrangler.toml`의 `[vars]`에 있습니다.

| 이름 | 기본값 | 설명 |
| --- | --- | --- |
| `CLAUDE_MODEL` | `claude-haiku-5-5` | 모델명. 품질 비교 후 `claude-sonnet-5-5` 등으로 바꿀 수 있다 |
| `CLAUDE_EFFORT` | `low` | 응답 깊이. 낮을수록 빠르다 |
| `CLAUDE_TIMEOUT_MS` | `9000` | Claude 호출 제한 시간. 앱의 10초보다 짧게 둔다 |
| `RATE_LIMIT_PER_MINUTE` | `6` | 기기당 분당 호출 횟수(이야기와 카드를 따로 센다). 기기 테스트 후 조정 |
| `CARD_TTL_DAYS` | `30` | 결과 카드 보관 기간(일) |

## 알아 둘 점

- 호출 제한은 Worker 인스턴스 메모리에 기록합니다. 인스턴스가 바뀌면 기록이 사라지므로 엄밀한 제한은 아닙니다. 지출 상한은 Anthropic 콘솔의 월 한도로 겁니다.
- 토큰이 유출되면 `npx wrangler secret put APP_SHARED_TOKEN`으로 새 값을 등록하고 앱 쪽 값도 바꿉니다.
- 이름·이야기 규칙은 `src/rules.js`에 모여 있습니다. 바꾸면 Unity 쪽 검사 규칙도 함께 맞춥니다.
