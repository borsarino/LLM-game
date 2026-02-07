using Python.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI; // Button用
using TMPro;           // TextMeshPro用
//using UnityEngine.UIElements;
using System.Text.RegularExpressions;

public class gameManagerVer2 : MonoBehaviour
{
    private SemaphoreSlim _semaphoreSlim;

    [Header("UI References")]
    public TMP_InputField inputField;
    public Button sendButton;
    public TextMeshProUGUI aiResponseDisplay;   //追加
    public GameObject clearPanel; // ★追加：クリア画面のパネル
    public GameObject gameoverPanel; // ★追加：クリア画面のパネル

    // 会話履歴を保持するリスト
    private List<Dictionary<string, string>> _chatHistory = new List<Dictionary<string, string>>();

    void OnEnable()
    {
        _semaphoreSlim = new SemaphoreSlim(1, 1);   // 同時に1つのスレッドだけがアクセス可能
    }

    void Start()
    {
        // ボタンにクリックイベントを登録
        if (sendButton != null)
            sendButton.onClick.AddListener(OnSendButtonClicked);

        // 最初のシステム設定を履歴に追加
        _chatHistory.Add(new Dictionary<string, string> {
            { "role", "system" },
            { "content", 
                "あなたはゲームの操作アシスタントのあっしー君です。" +
                "ユーザーの入力を解析し、移動に関する指示が来た場合必ず「MOVE(方向, 距離)」のフォーマットを会話とは関係なしに含めて回答してください。" +
                "方向は UP, DOWN, LEFT, RIGHT, FORWARD, BACKのいずれか、距離は数値です。特に指示がなければ数値は3にしてください。" +
                "いかなる時もユーザーの入力と同じ言語で友達のように返答してください。" +
                "このゲームは自身が動かす物体をゴールとなる白い立方体まで運べばクリアです。" +
                "紫色のとげに当たるとゲームオーバーです。" +
                "ユーザーの入力自体の形式は自由です"
            }
        });
    }

    // ボタンが押された時の処理
    public void OnSendButtonClicked()
    {
        string text = inputField.text;
        if (!string.IsNullOrEmpty(text))
        {
            LlamaReply(text); // ユーザーの入力を引数で渡す
            inputField.text = ""; // 入力欄をクリア
        }
    }

    void Update()
    {

    }

    // 引数を受け取るように変更
    public async void LlamaReply(string userContent)
    {
        //if (_isCleared) return; // クリア済みなら何もしない

        // ユーザーの発言を履歴に追加
        _chatHistory.Add(new Dictionary<string, string> { { "role", "user" }, { "content", userContent } });

        // ★追加：AIが考え始めたら画面に「Thinking...」と表示する
        if (aiResponseDisplay != null) aiResponseDisplay.text = "Thinking...";

        IntPtr? state = null; // Pythonのスレッド状態を保持する変数
        try
        {
            await _semaphoreSlim.WaitAsync(destroyCancellationToken); // 排他制御
            state = PythonEngine.BeginAllowThreads(); // Pythonのスレッド制御

            // 履歴リストごとPythonへ渡す
            string resultText = await Task.Run(() => LlamaPython(_chatHistory));

            // AIの回答を履歴に追加
            _chatHistory.Add(new Dictionary<string, string> { { "role", "assistant" }, { "content", resultText } });

            // ★追加：AIの回答を画面上のUIに表示する
            if (aiResponseDisplay != null)
            {
                aiResponseDisplay.text = resultText;
            }

            Debug.Log($"AI: {resultText}");
            ExecuteCommand(resultText.Trim());
        }
        catch (Exception e)
        {
            // ★追加：エラー時も画面に表示する
            if (aiResponseDisplay != null) aiResponseDisplay.text = "Error: " + e.Message;
            Debug.LogError($"エラーが発生しました: {e.Message}");
        }
        finally
        {
            if (state.HasValue) PythonEngine.EndAllowThreads(state.Value);
            _semaphoreSlim.Release();
        }
    }

    // 履歴リストを受け取るように引数を変更
    private string LlamaPython(List<Dictionary<string, string>> history)
    {
        string modelPath = Application.streamingAssetsPath + "/GGUF/Llama-3-ELYZA-JP-8B-q4_k_m.gguf"; // モデルのパスを指定

        using (Py.GIL())
        {
            using dynamic sample = Py.Import("sampleVer2"); // Pythonモジュールをインポート
            // history（C#のList）をそのままPython側に渡す
            using dynamic result = sample.llamaCppPython(modelPath, history);
            return result.ToString();
        }
    }

    // AIの返答を解析してコマンドを実行
    private void ExecuteCommand(string aiResponse)
    {
        // 正規表現で MOVE(方向, 距離) の中身を取り出す
        // 例: MOVE(RIGHT, 5) -> Group1: RIGHT, Group2: 5
        Match match = Regex.Match(aiResponse, @"MOVE\((UP|DOWN|LEFT|RIGHT|FORWARD|BACK),\s*(\d+)\)");

        if (match.Success)
        {
            string direction = match.Groups[1].Value;
            float distance = float.Parse(match.Groups[2].Value);

            Debug.Log($"解析成功: {direction} へ {distance} 移動します");

            // 実際の移動処理へ
            MoveCharacter(direction, distance);

            // 3. 【重要】MOVE(...) の部分だけを消した文章を作る
            string speechText = Regex.Replace(aiResponse, @"MOVE\(.*?\)", "").Trim();

            // もしMOVE以外に何も喋っていなければ、標準的な返答をセット
            if (string.IsNullOrEmpty(speechText)) speechText = "移動するね！";

            if (aiResponseDisplay != null) aiResponseDisplay.text = speechText;
        }
        else
        {
            Debug.Log($"{aiResponse}");
            // MOVEがない場合はそのまま表示
            if (aiResponseDisplay != null) aiResponseDisplay.text = aiResponse;
        }
    }

    private void MoveCharacter(string direction, float distance)
    {
        Vector3 moveVector = Vector3.zero;

        switch (direction)
        {
            case "UP": moveVector = Vector3.up; break;
            case "DOWN": moveVector = Vector3.down; break;
            case "LEFT": moveVector = Vector3.left; break;
            case "RIGHT": moveVector = Vector3.right; break;
            case "FORWARD": moveVector = Vector3.forward; break;
            case "BACK": moveVector = Vector3.back; break;
        }

        /*// 瞬間的に移動させる場合
        transform.Translate(moveVector * distance);*/

        if (moveVector != Vector3.zero)
        {
            // 直接動かすのではなく、コルーチン（時間差処理）を開始する
            StartCoroutine(SmoothMove(moveVector, distance));
        }
    }

    private IEnumerator SmoothMove(Vector3 direction, float distance)
    {
        float duration = 1.0f; // 何秒かけて移動するか
        float elapsed = 0f;
        Vector3 startPosition = transform.position;
        Vector3 targetPosition = startPosition + (direction * distance);

        // 移動中にその方向を向かせる
        //transform.rotation = Quaternion.LookRotation(direction);

        while (elapsed < duration)
        {
            // 時間経過に合わせて位置を補完する（Lerp）
            transform.position = Vector3.Lerp(startPosition, targetPosition, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null; // 1フレーム待機
        }

        // 最後に位置をぴったり合わせる
        transform.position = targetPosition;
    }

    private bool _isCleared = false; // クリアフラグ

    //ゴール判定
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Goal")) // ゴールに "Goal" タグをつけておきます
        {
            _isCleared = true; // クリアしたことを記録
            Debug.Log("ゴールに到達しました！");

            // ★クリア画面を表示する
            if (clearPanel != null)
            {
                clearPanel.SetActive(true);
            }

            // AIの返答表示欄にクリアメッセージを出す
            if (aiResponseDisplay != null)
            {
                aiResponseDisplay.text = "ゴールに到着！ゲームクリアです！";
            }

            /* 送信ボタンを無効化して、これ以上命令できないようにする
            if (sendButton != null)
            {
                sendButton.interactable = false;
            }

            // 必要に応じて入力を受け付けなくする
            if (inputField != null)
            {
                inputField.interactable = false;
            }*/
        }

        if (other.CompareTag("bad")) 
        {
            _isCleared = true; // クリアしたことを記録
            Debug.Log("失敗しました");

            // ★クリア画面を表示する
            if (gameoverPanel != null)
            {
                gameoverPanel.SetActive(true);
            }

            // AIの返答表示欄にクリアメッセージを出す
            if (aiResponseDisplay != null)
            {
                aiResponseDisplay.text = "敵に当たってしまった。ゲームオーバーです。";
            }

            /* 送信ボタンを無効化して、これ以上命令できないようにする
            if (sendButton != null)
            {
                sendButton.interactable = false;
            }

            // 必要に応じて入力を受け付けなくする
            if (inputField != null)
            {
                inputField.interactable = false;
            }*/
        }
    }
}