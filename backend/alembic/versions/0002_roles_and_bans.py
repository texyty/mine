"""Add creator role and account bans.

Revision ID: 0002
Revises: 0001
"""
from alembic import op
import sqlalchemy as sa


revision = "0002"
down_revision = "0001"
branch_labels = None
depends_on = None


def upgrade() -> None:
    bind = op.get_bind()
    if bind.dialect.name == "postgresql":
        with op.get_context().autocommit_block():
            op.execute("ALTER TYPE userrole ADD VALUE IF NOT EXISTS 'creator'")
    op.add_column("users", sa.Column("is_banned", sa.Boolean(), nullable=False, server_default=sa.false()))
    op.add_column("users", sa.Column("ban_reason", sa.String(length=300), nullable=True))
    op.execute("UPDATE users SET role = 'creator', has_access = true WHERE lower(username) = 'texyty'")


def downgrade() -> None:
    op.execute("UPDATE users SET role = 'admin' WHERE role = 'creator'")
    op.drop_column("users", "ban_reason")
    op.drop_column("users", "is_banned")
