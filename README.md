# Unity 환경 구성
---
### lab
<img width="1320" height="547" alt="image" src="https://github.com/user-attachments/assets/bae7a760-426c-4840-9a60-b80d836f4485" />
<img width="1144" height="518" alt="image" src="https://github.com/user-attachments/assets/c6393a7b-bae2-4834-a219-a4814f17d997" />

---

### hallway
<img width="1105" height="505" alt="image" src="https://github.com/user-attachments/assets/41fd4cb8-2f00-448a-bbac-0b28ea4f1bca" />
<img width="1180" height="509" alt="image" src="https://github.com/user-attachments/assets/32d1e9d1-1413-4e45-99eb-baf28b5104b0" />

---

### classroom
<img width="1122" height="515" alt="image" src="https://github.com/user-attachments/assets/fac3b4ec-fe85-453d-be58-4d6761fe1211" />

---

### library
<img width="1127" height="509" alt="image" src="https://github.com/user-attachments/assets/13ecc2c0-da82-47f6-a730-9dcf32a3f54c" />

---

# Implementation

---

### move
로봇을 특정 오브젝트 앞으로 이동시키는 기능
- NavMeshAgent를 활용해 경로를 계산하고 이동 수행
- move desk_01과 같은 명령을 입력하면 해당 위치를 탐색 후 로봇이 

https://github.com/user-attachments/assets/9d129628-e26a-4d33-b04d-e941be7ed3ff

---

### pick
로봇이 물체를 집는 기능
- HandTransform에 object를 attach시킨다
- pick book_01과 같은 명령을 입력하면 book_01이 손에 붙는 동작 수행
  
https://github.com/user-attachments/assets/5fe8a444-0b40-4d1f-b5eb-be924d136db9

---

### place
로봇이 들고 있는 물체를 지정된 위치에 내려놓는 기능
- 충돌 감지를 통해 테이블, 선반 등의 표면 위에 배치되도록 구현
- 자연스러운 place를 위해 target object와 robot의 벡터를 계산하여 로봇 관점에서 가장 가까운 가장자리에 배치
- place desk_01과 같은 명령을 입력하면 손에 들고 있는 물체를 desk_01위에 올려놓음

https://github.com/user-attachments/assets/38ef61f5-51d7-48f3-b4ec-832cc1b748b4

---

### open/close
문을 열고 닫는 기능
- asset store에 있는 door 스크립트 활용
- open labdoor_01 / close classroomdoor_01 과 같은 명령어 입력

https://github.com/user-attachments/assets/e120610e-aff2-4a26-b879-08c1d7914088

---

### switch/switchon/switchoff
방 안에 있는 light를 켜고 끄는 기능
- Unity Light Component와 연결해 On/Off 상태 변경
- switch light_01 / switchon light_02 / switchoff light_03 과 같은 명령어 입력
  
https://github.com/user-attachments/assets/5c5c0695-f3f7-484c-a8ac-1ca71dcb5779

---

### export
시뮬레이션 내 로봇과 객체 상태를 JSON형태로 추출하는 명령
- LLM 플래너가 최신 월드 상태를 읽어 grounding/플래닝에 활용할 수 있도록 설계
- 로봇 좌표, 오브젝트 위치, light상태, open/close 상태 등을 기록
- 통신을 통해 LLM planner에게 정보를 업데이트 해주기 위한 기능
- AABB를 이용해 static objcet와 object간의 관계를 파악하여 object에 on 상태 부여

--- 

### LLM Planner와 연결 후 싷행

https://github.com/user-attachments/assets/3bc8a8f2-9100-4d93-af4a-8153a17739dc


