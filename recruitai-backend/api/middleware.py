import jwt
import os
from rest_framework import authentication
from rest_framework import exceptions
from rest_framework import permissions
from api.db import users_col
from bson import ObjectId

JWT_SECRET = os.getenv("JWT_SECRET") or "REPLACE_WITH_32+_CHAR_SECRET_KEY_HERE!!"
JWT_ISSUER = os.getenv("Jwt__Issuer") or "Recruitero"
JWT_AUDIENCE = os.getenv("Jwt__Audience") or "Recruitero.Clients"

class UserAuthPayload:
    """Mock User class for request.user compatibility in Django"""
    def __init__(self, email, role, full_name):
        self.email = email
        self.role = role
        self.full_name = full_name
        self.is_authenticated = True

    def __str__(self):
        return f"{self.full_name} ({self.role})"

class JWTAuthentication(authentication.BaseAuthentication):
    def authenticate_header(self, request):
        return 'Bearer realm="api"'

    def authenticate(self, request):
        auth_header = request.META.get('HTTP_AUTHORIZATION')
        if not auth_header:
            return None

        parts = auth_header.split()
        if len(parts) != 2 or parts[0].lower() != 'bearer':
            raise exceptions.AuthenticationFailed('Invalid authorization header format')

        token = parts[1]
        try:
            payload = jwt.decode(
                token,
                JWT_SECRET,
                algorithms=['HS256'],
                audience=JWT_AUDIENCE,
                issuer=JWT_ISSUER
            )
        except jwt.ExpiredSignatureError as e:
            print("Token expired:", e)
            raise exceptions.AuthenticationFailed('Token has expired')
        except jwt.InvalidTokenError as e:
            print("Invalid token:", e, "Secret:", JWT_SECRET, "Aud:", JWT_AUDIENCE, "Iss:", JWT_ISSUER)
            raise exceptions.AuthenticationFailed('Invalid token')

        email = payload.get('email') or payload.get('sub')
        role = payload.get('http://schemas.microsoft.com/ws/2008/06/identity/claims/role') or payload.get('role')
        name = payload.get('name') or payload.get('unique_name')

        if not email or not role:
            raise exceptions.AuthenticationFailed('Token is missing required claims')

        user = UserAuthPayload(email=email, role=role, full_name=name or "")
        return (user, token)

# Custom Permissions classes matching the Roles defined in RecruitAI.Shared.Constants
class IsHRAdmin(permissions.BasePermission):
    def has_permission(self, request, view):
        if not (request.user and request.user.is_authenticated):
            raise exceptions.NotAuthenticated()
        return request.user.role == 'HRAdmin'

class IsTeamLead(permissions.BasePermission):
    def has_permission(self, request, view):
        if not (request.user and request.user.is_authenticated):
            raise exceptions.NotAuthenticated()
        return request.user.role == 'TeamLead'

class IsViewer(permissions.BasePermission):
    def has_permission(self, request, view):
        if not (request.user and request.user.is_authenticated):
            raise exceptions.NotAuthenticated()
        return request.user.role == 'Viewer'

class IsHrAdminOrTeamLead(permissions.BasePermission):
    def has_permission(self, request, view):
        if not (request.user and request.user.is_authenticated):
            raise exceptions.NotAuthenticated()
        return request.user.role in ['HRAdmin', 'TeamLead']

class IsHrAdminOrTeamLeadOrViewer(permissions.BasePermission):
    def has_permission(self, request, view):
        if not (request.user and request.user.is_authenticated):
            raise exceptions.NotAuthenticated()
        return request.user.role in ['HRAdmin', 'TeamLead', 'Viewer']
