"""Optional real Google model availability check, using Application Default Credentials.
Runs one small billable request. Does NOT replace the real Unity acceptance test.
"""
import os
from google import genai
from google.genai import types

client = genai.Client(vertexai=True, project=os.environ["GOOGLE_CLOUD_PROJECT"], location="global")
response = client.models.generate_content(model="gemini-3.5-flash", contents="Reply with OK.",
                                         config=types.GenerateContentConfig(max_output_tokens=128))
print(response.text)
