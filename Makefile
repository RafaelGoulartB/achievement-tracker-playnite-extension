.PHONY: help build clean rebuild

help:
	@echo "Available commands:"
	@echo "  make build    - Compile the Achievement Tracker extension"
	@echo "  make clean    - Clean the build artifacts"
	@echo "  make rebuild  - Clean and recompile the extension"

build:
	dotnet build src/AchievementTracker.csproj

clean:
	dotnet clean src/AchievementTracker.csproj

rebuild: clean build
