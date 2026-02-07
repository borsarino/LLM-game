import llama_cpp
from llama_cpp import Llama

# モデルのインスタンスを再利用するためのグローバル変数
_model_cache = None

def llamaCppPython(modelPath, messages_list):
    global _model_cache
    
    # モデルの読み込みを初回のみにする（高速化）
    if _model_cache is None:
        _model_cache = Llama(
            model_path=modelPath,
            chat_format="llama-3",
            n_ctx=2048, # 履歴が長くなるので少し広めに
        )

    # C#から渡された messages_list をそのまま使う
    response = _model_cache.create_chat_completion(
        messages=messages_list,
        max_tokens=1024,
    )

    return response["choices"][0]["message"]["content"]