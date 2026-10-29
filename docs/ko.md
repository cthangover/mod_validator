# Validator — 게임을 하지 않고 모드 검사하기

Validator는 실행 중인 게임의 모든 모드 — 매니페스트, 데이터 파일, 시나리오, 장면 패치, C# 소스 — 를 읽고, 깨졌거나 수상한 것을 보고합니다. 아무도 선언하지 않는 아이템·장면·배경 참조, 알 수 없는 시나리오 명령, 잘못된 매개변수, 빠진 의존성. 게임이 시작될 때마다 백그라운드로 돌고 보고서를 쓰므로, 오타는 플레이어가 그 줄에 닿기 전에 발견됩니다.

검사하는 것은 파일에 보이는 것뿐입니다. Validator는 게임을 플레이하지 않습니다: 통과시킨 시나리오라도 작성자의 의도와 다르게 동작할 수 있고, 실행 중에 id를 만들어 내는 코드는 쫓을 수 없습니다.

## 실수와 그 바로잡기

플레이어에게 늑대 고기를 주는 시나리오 줄. 아이템 id에 오타가 있습니다:

```scenario
scene: home_kitchen
priority: 0
---
action item.add id=food/wolf_meet count=1
end
```

다음에 Cthangover를 시작하면 보고서에 이것이 담깁니다:

```json
{
  "severity": "error",
  "code": "REF_NOT_FOUND",
  "message": "item 'food/wolf_meet' is referenced but never declared",
  "mod": "cthangover_game",
  "file": "scenarios/doc_validator_demo.scenario",
  "line": 4,
  "suggestion": "declare item 'food/wolf_meet' or fix the reference",
  "related": null
}
```

모드, 파일, 줄이 실수를 가리킵니다. `food/wolf_meat`로 고치면 문제가 사라지고, 요약의 오류 개수가 하나 줄어듭니다(이번 실행에서는 18에서 17로).

## 실행 방법과 보고서 읽기

- 그것을 포함하는 게임에 들어가면(Cthangover는 개발자 도구와 함께 넣어 둡니다) 모드가 불러와진 뒤 저절로 시작되고, 백그라운드 스레드에서 돕니다. 게임을 막지 않습니다.
- 로그에는 한 번 돌 때마다 한 줄이 남습니다: `validator: started, 33 mod(s) queued`, 그다음에 오류·경고·정보의 총계.
- 보고서는 `user://validation/report.json`(게임 사용자 데이터 폴더)에 기록됩니다. 요약, 버전과 체크섬이 담긴 검사된 모드들, 문제 목록이 들어 있습니다.

문제마다 다음이 있습니다:

- `severity` — `error`(무언가 깨짐), `warning`(아마 실수), `info`(한 번 볼 만함. 예컨대 아무도 참조하지 않는 아이템);
- `code` — 문제의 종류. 예: `REF_NOT_FOUND`, `JSON_PARSE_ERROR`, `SCHEMA_MISSING_FIELD`, `SCHEMA_UNKNOWN_PARAM`, `DECL_UNUSED`, `DYNAMIC_KEY`;
- `mod`, `file`, `line` — 어디를 고칠지; `suggestion` — 무엇을 할지.

`DYNAMIC_KEY` 정보는 키가 실행 중에 만들어져 Validator가 검사할 수 없다는 뜻입니다. 오류가 아닙니다.

## 무엇을 검사하는가

검사는 세 단계로 돕니다:

1. **스키마** — `manifest.json`에 문자열 `id`와 `version`이 있음; JSON 파일이 해석됨.
2. **계약** — 각 모드가 무엇을 선언하고 무엇을 참조하는지: 모든 종류의 엔티티(캐릭터, 아이템, 퀘스트, 스킬, 제작법, 행동, 그리고 스키마가 설명한 종류), 시나리오 명령과 행동과 그 매개변수, 장면 패치, C# 코드가 읽고 쓰는 속성.
3. **대조** — 모든 참조에 선언이 있는지, `depends`가 모드가 남에게서 쓰는 것을 커버하는지, 아무도 쓰지 않는 선언 목록.

시나리오 행동과 매개변수는 모드가 담는 스키마와 비교해(Schemas 참고) 명령 팔레트와 같은 규칙으로 검사됩니다.

## 설정

모드 관리자에서 이 모드의 설정 탭에서:

- **Enabled**, **Run on start** — 실행을 끕니다;
- **Parse C#**, **Parse GDScript**(기본값 꺼짐, 느림), **Parse scenarios** — 어떤 소스를 읽을지;
- **Write report**, **Report path** — `report.json`이 가는 곳;
- **Block mods on error** — 설정에는 있지만 현재 버전에서는 아무것도 바꾸지 않습니다: 모드는 막히지 않습니다.

## 문제가 생겼을 때

1. **보고서가 없습니다.** 실행이 끝나기 전에 게임이 닫혔거나(Cthangover는 30초쯤 걸립니다) **Write report**가 꺼져 있습니다.
2. **`DYNAMIC_KEY` 줄이 많습니다.** 키를 만들어 내는 코드에서는 정상입니다. 정보입니다.
