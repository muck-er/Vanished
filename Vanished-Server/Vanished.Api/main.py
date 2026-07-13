from __future__ import annotations

from contextlib import asynccontextmanager
import logging
from datetime import datetime, timezone

from fastapi import FastAPI, HTTPException, Query, Request, WebSocket
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse, ORJSONResponse

from core.config import get_settings
from core.database import init_database
from core.redis import redis_client
from core.security import verify_ws_token
from app.security.rate_limit import enforce_websocket_rate_limit
from routers import auth, chat, messages, export
from ws.bridge import configure as configure_ws_bridge
from ws.manager import connection_manager


@asynccontextmanager
async def lifespan(app: FastAPI):
    init_database()
    await redis_client.initialize()
    configure_ws_bridge(connection_manager)
    yield
    await redis_client.close()


settings = get_settings()
logger = logging.getLogger("vanished.api")


class _NoisyAccessLogFilter(logging.Filter):
    NOISY_PATHS = (
        "/favicon.ico",
    )

    def filter(self, record: logging.LogRecord) -> bool:
        try:
            args = getattr(record, "args", ()) or ()
            path = str(args[2]) if len(args) >= 3 else record.getMessage()
            status = int(args[4]) if len(args) >= 5 else 0
            if 200 <= status < 400 and any(item in path for item in self.NOISY_PATHS):
                return False
        except Exception:
            return True
        return True


logging.getLogger("uvicorn.access").addFilter(_NoisyAccessLogFilter())
app = FastAPI(title="Vanished API", lifespan=lifespan, default_response_class=ORJSONResponse)

app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.cors_origins,
    allow_credentials=True,
    allow_methods=["GET", "POST", "OPTIONS"],
    allow_headers=[
        "Authorization",
        "Content-Type",
        "X-Vanished-Body-SHA256",
        "X-Vanished-Device-Id",
        "X-Vanished-Nonce",
        "X-Vanished-Signature",
        "X-Vanished-Timestamp",
    ],
    expose_headers=["Content-Disposition"],
    max_age=600,
)


@app.middleware("http")
async def add_security_headers(request: Request, call_next):
    response = await call_next(request)
    response.headers.setdefault("X-Content-Type-Options", "nosniff")
    response.headers.setdefault("X-Frame-Options", "DENY")
    response.headers.setdefault("Referrer-Policy", "no-referrer")
    response.headers.setdefault("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=(), usb=()")
    if request.url.path.startswith("/api/"):
        response.headers.setdefault("Cache-Control", "no-store")
    return response


@app.exception_handler(HTTPException)
async def http_exception_handler(_request, exc: HTTPException):
    message = exc.detail if isinstance(exc.detail, str) else "Pedido inválido."
    return JSONResponse(status_code=exc.status_code, content={"success": False, "message": message})




@app.exception_handler(Exception)
async def unhandled_exception_handler(request, exc: Exception):
    logger.error("Unhandled API error on %s %s: %s", getattr(request, "method", "?"), getattr(request, "url", "?"), exc, exc_info=True)
    return JSONResponse(
        status_code=500,
        content={
            "success": False,
            "message": "Erro interno no servidor.",
        },
    )


app.include_router(auth.router)
app.include_router(chat.router)
app.include_router(messages.router)
app.include_router(export.router)


@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket, token: str = Query(...)):
    payload = verify_ws_token(token)
    if not payload:
        await websocket.close(code=4001)
        return
    user_id = str(payload["sub"])
    try:
        await enforce_websocket_rate_limit(websocket, user_id)
    except Exception:
        await websocket.close(code=4429)
        return
    await connection_manager.connect(user_id, websocket)
    try:
        await connection_manager.listen(user_id, websocket)
    finally:
        await connection_manager.disconnect(user_id, websocket)


@app.get("/")
async def root():
    return {"success": True, "message": "Vanished API online", "health": "/health", "websocket": "/ws"}


@app.get("/health")
async def health():
    ws_connections = sum(len(sockets) for sockets in connection_manager._connections.values())
    return {
        "success": True,
        "status": "ok",
        "message": "Vanished FastAPI online",
        "timestamp": datetime.now(timezone.utc).isoformat(),
        "zk": True,
        "websocket": True,
        "ws_connections": ws_connections,
    }


@app.get("/fastapi-health")
async def fastapi_health():
    return {"success": True, "message": "Vanished FastAPI online", "websocket": True}
