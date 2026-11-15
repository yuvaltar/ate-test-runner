# מדריך לימוד – ATE Test Runner

המטרה: שתוכל להסביר כל שורה בפרויקט בראיון. כדאי לעבור לפי הסדר.

## 1. מה הפרויקט עושה (משפט אחד לראיון)

"כתבתי ב-C# כלי שמריץ בדיקות ייצור על יחידה אלקטרונית: הוא מתחבר אליה ב-TCP או בסריאל,
שולח פקודות בסגנון SCPI, בודק כל מדידה מול גבולות שמוגדרים בקובץ JSON, ומוציא דוח PASS/FAIL
לפי מספר סידורי. בניתי גם סימולטור של היחידה עם הזרקת תקלות, ו-35 טסטים שרצים ב-CI."

## 2. סדר קריאת הקוד

| # | קובץ | מה ללמוד ממנו |
|---|------|---------------|
| 1 | `sequences/opt450_end_of_line.json` | איך נראית בדיקת ייצור: איפוס, זיהוי, גרסה, מדידות, ניקוי |
| 2 | `src/Ate.DutSimulator/DutModel.cs` | הפרוטוקול: איזו פקודה מחזירה מה. `switch` פשוט |
| 3 | `src/Ate.Core/Sequencing/TestSequence.cs` | טעינת JSON ל-class, ולידציה לפני שנוגעים בחומרה |
| 4 | `src/Ate.Core/Sequencing/StepEvaluator.cs` | הלוגיקה של עובר/נכשל. פונקציה טהורה, בלי I/O |
| 5 | `src/Ate.Core/Sequencing/TestRunner.cs` | הלב: לולאה על הצעדים, retries, stop-on-fail, `alwaysRun` |
| 6 | `src/Ate.Core/Transport/StreamLineTransport.cs` | **החלק הכי חשוב לראיון** – ראה סעיף 4 |
| 7 | `src/Ate.Core/Transport/TcpTransport.cs`, `SerialTransport.cs` | ירושה: רק פתיחה וסגירה שונות |
| 8 | `src/Ate.Cli/Program.cs` | ארגומנטים, הדפסה צבעונית, exit codes |
| 9 | `tests/Ate.Tests/*` | איך בודקים בלי חומרה: Fake transport + סימולטור אמיתי |

## 3. מושגי C# שכדאי לדעת (מול Java שאתה כבר מכיר)

- `async` / `await` / `Task<T>` – כמו `CompletableFuture`, אבל נקי יותר. כל I/O כאן אסינכרוני.
- `CancellationToken` – הדרך הסטנדרטית לבטל פעולה (Ctrl+C, timeout).
- `record` – class לנתונים, immutable, עם `Equals` מוכן (`StepResult`, `RunResult`).
- `init` / `required` – מאפיינים שאפשר לקבוע רק ביצירה / חובה לקבוע.
- `interface ITransport : IAsyncDisposable` + `await using` – כמו try-with-resources ב-Java.
- `event Action<StepResult>` – callback; ה-CLI נרשם אליו כדי להדפיס התקדמות.
- `switch` expression ו-pattern matching: `s.Status is StepStatus.Fail or StepStatus.Error`.
- `Channel<T>` – תור thread-safe בין producer ל-consumer (כמו `BlockingQueue`).
- LINQ: `Steps.All(...)`, `Count(...)`, `First(...)` – כמו Java Streams.

## 4. ההחלטה הטכנית שכדאי להציג בראיון

**הבעיה:** שולחים `MEAS:TEMP?`, היחידה איטית ואין תשובה תוך 500ms, אז מוותרים.
אחרי 600ms התשובה מגיעה בכל זאת. את הפקודה הבאה (`RELAY OFF`) הקוד קורא את השורה הבאה,
אבל זו התשובה הישנה של הטמפרטורה. מכאן כל התוצאות זזות באחד, והיחידה מקבלת דוח שגוי.

**הפתרון:** לולאת רקע (`ReadLoopAsync`) קוראת כל שורה שמגיעה ומכניסה אותה ל-`Channel`.
לפני כל פקודה חדשה מרוקנים את התור (`TryRead` בלולאה), כך שתשובות מאוחרות נזרקות.

**ההוכחה:** הטסט `Late_response_is_discarded_and_does_not_shift_the_next_answer`.
אם מוחקים את שורת הריקון, הטסט נכשל ("Expected FAST, got LATE"). בדקתי את זה.

## 5. שאלות שסביר שישאלו – ותשובות

- **למה JSON ולא קוד?** מהנדס בדיקות משנה גבולות או מוסיף צעד בלי לקמפל מחדש.
- **למה retries?** מתח או זרם צריכים זמן להתייצב אחרי סגירת ממסר. `delayMs` + `retries` מטפלים בזה.
  ניתוק חיבור, לעומת זאת, לא מנסים שוב.
- **למה `alwaysRun`?** אם בדיקה נכשלה באמצע, עדיין צריך לפתוח את הממסר ולהשאיר יחידה בטוחה.
- **למה `InvariantCulture`?** במחשב עם הגדרות אזוריות שבהן הנקודה העשרונית היא פסיק (למשל גרמניה), "39.5" לא יתפרסר נכון בלעדיו. יש על זה טסט.
- **איך בודקים בלי חומרה?** שתי רמות: `FakeTransport` עם תשובות מתוסרטות (מהיר, דטרמיניסטי),
  וסימולטור אמיתי על socket (end-to-end, כולל כל תקלה מוזרקת).
- **מה היית מוסיף?** הרצה במקביל על כמה יחידות בג'יג, שמירת תוצאות ל-DB, GUI ב-WPF, חתימת דוחות,
  תמיכה ב-Modbus או CAN.

## 6. תרגילים (אחרי כל אחד: `dotnet test`)

1. הוסף לסימולטור פקודה `MEAS:FREQ?` והוסף צעד לרצף.
2. הוסף תקלה `iout-low` לסימולטור וטסט שמוודא שהיא נתפסת.
3. הוסף ל-CLI דגל `--loop N` שמריץ את הרצף N פעמים ומדפיס yield (אחוז עוברים).
4. הוסף סוג צעד `wait` שרק מחכה X מילישניות.

אם תעשה את 1–3 בעצמך, תכיר את הקוד מספיק טוב לראיון.

## 7. הרצה אצלך (Windows)

1. התקן .NET 8 SDK.
2. `dotnet test` מתיקיית הפרויקט.
3. שני טרמינלים: סימולטור ו-CLI, לפי ה-README.
4. העלה ל-GitHub (`yuvaltar/ate-test-runner`) ובדוק שה-CI ירוק.
