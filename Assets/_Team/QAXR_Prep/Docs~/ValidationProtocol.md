# W5: Validation protocol (Sat 21 Nov, morning)

**Goal:** check that QAXR's risk score moves the same way as people's sickness reports. Zone 1 has a *bad* and a *fixed* condition. We report the results **descriptively**: n = 3–5, no significance tests, and no claim that the score predicts sickness.

## Roles and kit

- **Experimenter** runs the build, reads the script, and watches the participant the whole time.
- **Note-taker** fills in the SSQ sheet and logs times.
- **Kit:** Quest 3 with the Zone 1 build (`SicknessRatingPanel` on, `MotionRecorder` on), a swivel chair, water, a bin bag, paper SSQ sheets (or `ssq_score.py --template`), a timer.

## Participants

- 3–5 people from other teams, aged 18 or over.
- **Do not include** anyone who is pregnant, has epilepsy or a history of seizures, has a vestibular or balance disorder, has a migraine today, or feels unwell today.
- Assign IDs `P01`–`P05`. Counterbalance the order: odd IDs do bad → fixed, even IDs do fixed → bad.

## Procedure (about 15 min per person)

| Step | Time | What |
|---|---|---|
| 1 | 2 min | Consent (below). Make sure the participant knows they can stop at any time |
| 2 | 1 min | **Baseline SSQ** (16 items, each 0–3) |
| 3 | 1 min | Headset on, seated. Practice pressing the rating panel once |
| 4 | ≤ 2 min | **Condition A**: steer through Zone 1 with the left thumbstick. The panel asks for a rating every 30 s |
| 5 | 1 min | Headset off. **SSQ** |
| 6 | ≥ 2 min | Break. Continue only when the participant says they are back to their baseline |
| 7 | ≤ 2 min | **Condition B**, then the SSQ again |
| 8 | 1 min | Ask "Which condition felt worse, and why?" Write down the answer verbatim |

**Stop rule:** stop immediately, take the headset off and end the session if the participant asks, rates 7 or more on the panel, or looks pale or sweaty. Keep them seated until they feel fine. A stopped session is still valid data. Record the time it stopped.

## Data

- **Files:** ratings in `qaxr/ratings/<P>_<cond>_*.csv`, motion traces in `qaxr/<run>/`, and SSQ answers in one CSV with the columns `participant, condition, phase, q1..q16`. `phase` is `baseline` or `post`.
- **Analysis:**
  - `python ssq_score.py ssq.csv > ssq_scores.csv`
  - `python analyze_validation.py --ratings <dir> --risk <dir> --ssq ssq_scores.csv`
- **Storage:** no names in any file. The ID-to-name sheet stays on paper with the experimenter, and we destroy it after the hackathon.

## Consent text (read aloud, then ask for a "yes")

> We are testing a tool that estimates how likely a VR scene is to cause motion sickness. You will move through a short virtual corridor twice, about two minutes each time, seated, and answer a short symptom questionnaire before and after. Some people feel mild dizziness or nausea in VR. It usually passes within minutes. You can stop at any time without giving a reason, simply by saying "stop". We record your head movement in the headset, your ratings and your questionnaire answers, under a code and not your name. We use them only for this hackathon project and its write-up, and we delete them afterwards. Do you have any questions? Do you agree to take part?
