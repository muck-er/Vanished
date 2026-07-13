from __future__ import annotations

from fastapi.responses import JSONResponse


def api_response(payload: dict, status_code: int = 200) -> JSONResponse:
    return JSONResponse(content=payload, status_code=status_code)


def ok(payload: dict | None = None, message: str = "OK", status_code: int = 200) -> JSONResponse:
    data = {"success": True, "message": message}
    if payload:
        data.update(payload)
    return api_response(data, status_code)


def fail(message: str, status_code: int = 400, **extra) -> JSONResponse:
    data = {"success": False, "message": message}
    data.update(extra)
    return api_response(data, status_code)
