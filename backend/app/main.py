from pathlib import Path

from fastapi import FastAPI, UploadFile, File, HTTPException
from fastapi.middleware.cors import CORSMiddleware
import logging

from app.schemas import HealthResponse, InspectionResponse
from app.preprocessing import load_image, preprocess, to_tensor
from app.inference import run_inference

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

ALLOWED_EXTENSIONS = {".jpg", ".jpeg", ".png", ".bmp"}

app = FastAPI(
    title="Vision Inspection API",
    description="製造業向け外観検査画像解析API",
    version="1.0.0",
)

# 本番環境では allow_origins をフロントエンドのオリジンに限定すること。
# 例: allow_origins=["http://192.168.1.100"]
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["POST", "GET"],
    allow_headers=["*"],
)


@app.get("/health", response_model=HealthResponse)
def health():
    return HealthResponse(status="ok", message="Vision Inspection API is running")


@app.post("/inspect", response_model=InspectionResponse)
async def inspect(file: UploadFile = File(...)):
    suffix = Path(file.filename).suffix.lower() if file.filename else ""
    if suffix not in ALLOWED_EXTENSIONS:
        raise HTTPException(
            status_code=400,
            detail=f"非対応のファイル形式です。対応形式: {', '.join(ALLOWED_EXTENSIONS)}",
        )

    image_bytes = await file.read()
    if not image_bytes:
        raise HTTPException(status_code=400, detail="ファイルが空です。")

    try:
        img = load_image(image_bytes)
    except ValueError as e:
        raise HTTPException(status_code=400, detail=str(e))

    try:
        processed = preprocess(img)
        tensor = to_tensor(processed)
        result = run_inference(tensor)
    except Exception as e:
        logger.exception("推論処理中にエラーが発生しました")
        raise HTTPException(status_code=500, detail=f"推論処理に失敗しました: {e}")

    logger.info(
        "検査結果: %s score=%.4f defect=%s file=%s",
        result["result"], result["score"], result["defect_type"], file.filename,
    )
    return InspectionResponse(**result)
