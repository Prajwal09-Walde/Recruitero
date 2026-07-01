import json
import logging
import jwt
from urllib.parse import parse_qs
from channels.generic.websocket import AsyncWebsocketConsumer
from api.middleware import JWT_SECRET, JWT_AUDIENCE, JWT_ISSUER

logger = logging.getLogger(__name__)

# ASCII 30 (Record Separator) is the frame terminator in SignalR JSON Hub Protocol
RECORD_SEPARATOR = "\x1e"

class RecruitmentConsumer(AsyncWebsocketConsumer):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.joined_groups = set()

    async def connect(self):
        # Extract JWT token from query string
        query_string = self.scope.get("query_string", b"").decode("utf-8")
        params = parse_qs(query_string)
        token = params.get("access_token", [None])[0]

        if not token:
            logger.warning("WebSocket connection rejected: Missing access token")
            await self.close(code=4001)
            return

        try:
            # Decode and validate JWT
            payload = jwt.decode(
                token,
                JWT_SECRET,
                algorithms=["HS256"],
                audience=JWT_AUDIENCE,
                issuer=JWT_ISSUER
            )
            self.user_email = payload.get("email") or payload.get("sub")
            self.user_role = payload.get("http://schemas.microsoft.com/ws/2008/06/identity/claims/role") or payload.get("role")
            
            logger.info(f"WebSocket authenticated user: {self.user_email} ({self.user_role})")
            await self.accept()
        except Exception as e:
            logger.warning(f"WebSocket connection rejected: Invalid token: {e}")
            await self.close(code=4002)

    async def disconnect(self, close_code):
        # Leave all joined groups on disconnect
        for group_name in list(self.joined_groups):
            await self.channel_layer.group_discard(group_name, self.channel_name)
        logger.info("WebSocket disconnected")

    async def receive(self, text_data):
        # Handle incoming SignalR protocol messages
        # Messages end with the record separator character
        if not text_data.endswith(RECORD_SEPARATOR):
            return

        raw_messages = text_data.split(RECORD_SEPARATOR)
        for raw in raw_messages:
            if not raw or not raw.strip():
                continue
            
            try:
                msg = json.loads(raw)
            except Exception:
                continue

            # SignalR Protocol Handshake Request
            if msg.get("protocol") == "json" and msg.get("version") == 1:
                # Respond with empty JSON handshake completion frame
                await self.send(text_data=json.dumps({}) + RECORD_SEPARATOR)
                continue

            # Target Hub invocations (type 1 = Invocation)
            msg_type = msg.get("type")
            if msg_type == 1:
                target = msg.get("target")
                invocation_id = msg.get("invocationId")
                arguments = msg.get("arguments") or []

                if target == "JoinJobRoom" and arguments:
                    job_id = arguments[0]
                    group_name = f"job_{job_id}"
                    await self.channel_layer.group_add(group_name, self.channel_name)
                    self.joined_groups.add(group_name)
                    logger.info(f"Connection {self.channel_name} joined room {group_name}")

                    # Return empty invocation completion if requested
                    if invocation_id:
                        await self.send(text_data=json.dumps({
                            "type": 3,
                            "invocationId": invocation_id
                        }) + RECORD_SEPARATOR)

                elif target == "LeaveJobRoom" and arguments:
                    job_id = arguments[0]
                    group_name = f"job_{job_id}"
                    await self.channel_layer.group_discard(group_name, self.channel_name)
                    self.joined_groups.discard(group_name)
                    logger.info(f"Connection {self.channel_name} left room {group_name}")

                    if invocation_id:
                        await self.send(text_data=json.dumps({
                            "type": 3,
                            "invocationId": invocation_id
                        }) + RECORD_SEPARATOR)

    async def hub_message(self, event):
        """Sends broadcast events received from channels layer group to the WebSocket client."""
        target = event.get("target")
        arguments = event.get("arguments") or []

        # SignalR Server invocation frame
        msg = {
            "type": 1,
            "target": target,
            "arguments": arguments
        }
        await self.send(text_data=json.dumps(msg) + RECORD_SEPARATOR)
