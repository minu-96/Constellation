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
| 502 | Claude 호출 실패, 또는 답이 규칙에 맞지 않음 |

앱은 200이 아니면 모두 폴백 이야기로 넘어갑니다.

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
| `RATE_LIMIT_PER_MINUTE` | `6` | 기기당 분당 호출 횟수. 기기 테스트 후 조정 |

## 알아 둘 점

- 호출 제한은 Worker 인스턴스 메모리에 기록합니다. 인스턴스가 바뀌면 기록이 사라지므로 엄밀한 제한은 아닙니다. 지출 상한은 Anthropic 콘솔의 월 한도로 겁니다.
- 토큰이 유출되면 `npx wrangler secret put APP_SHARED_TOKEN`으로 새 값을 등록하고 앱 쪽 값도 바꿉니다.
- 이름·이야기 규칙은 `src/rules.js`에 모여 있습니다. 바꾸면 Unity 쪽 검사 규칙도 함께 맞춥니다.
