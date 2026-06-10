from pydantic import BaseModel


class HealthResponse(BaseModel):
    status: str
    message: str


class InspectionResponse(BaseModel):
    result: str          # "OK" or "NG"
    score: float         # 0.0 - 1.0
    defect_type: str     # "none" / "scratch" / "stain" / "crack" / "shape" / "label" / "unknown"
    message: str
    inference_ms: float = 0.0   # 推論処理時間 (ミリ秒)
