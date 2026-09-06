# 등불야행: 물의 정령을 찾아서
<img width="815" height="461" alt="등불야행" src="https://github.com/user-attachments/assets/b0eec356-afe9-48a7-8625-0ddbc63a8a8b" />
수원 행궁동 공방거리를 활성화하기 위해, 
공방거리 곳곳에 숨은 AR 정령을 포획하고 도감을 채우는 GPS 기반 모바일 게임
## 개요
- 기간: 2026.04 – 2026.06
- 팀: 5인팀(1인 개발)
- 역할: 클라이언트 전체 구현
- 기술: Unity 6 · C# · AR Foundation 6.5.0 (ARCore)
- **2026 로커톤 최우수상**
  
**영상 링크 포함 설명 링크:**
https://thinkable-pickle-e1c.notion.site/2026-35db6e28cc828002827fcbdb66129809?source=copy_link

## 게임 흐름
```mermaid
graph LR
  A[등불 선택] --> B[GPS 근접]
  B -->|OS 알림| C[AR 카메라]
  C --> D[정령 포획]
  D --> E[도감 수집]
  E --> F[결말 해금]
```
## 발표 자료
[행동대장_발표자료.pdf](https://github.com/user-attachments/files/31877278/_.pdf)

## 담당 내용
씬 전환 간 상태 관리: DontDestroyOnLoad 싱글톤에 진행상태 보관 + 도감 JSON 관리
생명주기 기반 AR 정령 배치: 세션이 추적 상태가 된 뒤에만 스폰 + ARAnchor 사용하여 공간 고정
좌표 판정: 하버사인 구현 + 매 프레임 대신 0.5초 주기 판정
정령 데이터 설계: 기획자가 코드 없이 정령을 추가, 수정할 수 있도록 ScriptableObject 에셋으로 분리

## 기술
기술 상세 내용은 이미지로 정리했습니다.
<img width="1920" height="1080" alt="제목을 입력해주세요" src="https://github.com/user-attachments/assets/1800c96b-2e38-4f61-b8a0-5ba8d3e8b851" />
<img width="1920" height="1080" alt="제목을 입력해주세요  (2)" src="https://github.com/user-attachments/assets/262ca971-0105-464a-8a1f-067c2cf7d5f8" />
<img width="1920" height="1080" alt="제목을 입력해주세요  (1)" src="https://github.com/user-attachments/assets/7f4656a6-a8b5-4bb2-a44e-915f2e09d4c8" />
