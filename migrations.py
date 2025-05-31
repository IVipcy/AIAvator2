from sqlalchemy import create_engine
from models import Base
from config import DATABASE_URL
import logging

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

def run_migrations():
    try:
        engine = create_engine(DATABASE_URL)
        logger.info("Creating database tables...")
        Base.metadata.create_all(engine)
        logger.info("Database tables created successfully!")
    except Exception as e:
        logger.error(f"Error during migration: {e}")
        raise

if __name__ == "__main__":
    run_migrations() 