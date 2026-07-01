from django.urls import path
from api import views

urlpatterns = [
    # Auth
    path('auth/register', views.register_view, name='register'),
    path('auth/login', views.login_view, name='login'),
    path('auth/refresh', views.refresh_view, name='refresh'),
    path('auth/logout', views.logout_view, name='logout'),
    path('auth/me', views.me_view, name='me'),
    path('auth/forgot-password', views.forgot_password_view, name='forgot_password'),
    path('auth/reset-password', views.reset_password_view, name='reset_password'),

    # Jobs
    path('jobs', views.jobs_list_create_view, name='jobs_list_create'),
    path('jobs/import-dummies', views.import_dummy_jobs_view, name='import_dummy_jobs'),
    path('jobs/analytics', views.analytics_view, name='analytics'),
    path('jobs/preview-skills', views.preview_skills_view, name='preview_skills'),
    path('jobs/<str:job_id>', views.job_detail_view, name='job_detail'),
    path('jobs/<str:job_id>/leaderboard', views.leaderboard_view, name='leaderboard'),
    path('jobs/<str:job_id>/applications/bulk-upload', views.bulk_upload_resumes_view, name='bulk_upload_resumes'),

    # Applications
    path('applications/<str:application_id>/interview-kit', views.interview_kit_view, name='interview_kit'),
    path('applications/<str:application_id>/interview-kit/regenerate', views.regenerate_interview_kit_view, name='regenerate_interview_kit'),
    path('applications/<str:application_id>/status', views.update_application_status_view, name='update_application_status'),

    # Webhooks
    path('webhooks', views.webhooks_list_create_view, name='webhooks_list_create'),
    path('webhooks/<str:webhook_id>', views.webhook_detail_view, name='webhook_detail'),
    path('webhooks/<str:webhook_id>/deliveries', views.webhook_deliveries_view, name='webhook_deliveries'),
]
