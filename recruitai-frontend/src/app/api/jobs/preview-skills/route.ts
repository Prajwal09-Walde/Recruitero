import { NextRequest, NextResponse } from 'next/server';
import axios from 'axios';

export async function GET(request: NextRequest) {
  const { searchParams } = new URL(request.url);
  const text = searchParams.get('text');

  if (!text) {
    return NextResponse.json({ error: 'Text query parameter is required' }, { status: 400 });
  }

  const backendUrl = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000';

  try {
    const response = await axios.get(`${backendUrl}/api/jobs/preview-skills`, {
      params: { text },
    });
    return NextResponse.json(response.data);
  } catch (error: any) {
    return NextResponse.json(
      { error: error.response?.data?.detail || 'Failed to communicate with backend' },
      { status: error.response?.status || 500 }
    );
  }
}
