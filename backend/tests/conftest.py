import os

os.environ["DATABASE_URL"] = "sqlite+aiosqlite:///./test_launcher_current.db"
os.environ["JWT_SECRET"] = "test-secret-that-is-long-enough-for-tests"
os.environ["BOOTSTRAP_ADMIN_USERNAME"] = "admin"
os.environ["BOOTSTRAP_ADMIN_EMAIL"] = "admin@example.com"
os.environ["BOOTSTRAP_ADMIN_PASSWORD"] = "very-secure-admin-password"
