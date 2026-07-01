from django.urls import re_path
from api import consumers

websocket_urlpatterns = [
    re_path(r'^hubs/recruitment$', consumers.RecruitmentConsumer.as_view()),
]
