import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';

type DocumentSummary = {
  id: string;
  fileName: string;
  sizeBytes: number;
  uploadedAt: string;
  totalPages: number;
  totalChunks: number;
  totalTokens: number;
  status: string;
};

type ChatResult = {
  answer: string;
  sources: { page: number; chunkIndex: number; preview: string }[];
};

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.css'
})
export class AppComponent {
  private api = 'http://localhost:5000/api';

  documents: DocumentSummary[] = [];
  selected?: DocumentSummary;
  question = '';
  messages: { role: 'user' | 'assistant'; text: string; sources?: any[] }[] = [];
  loading = false;

  constructor(private http: HttpClient) {
    this.loadDocuments();
  }

  loadDocuments() {
    this.http.get<DocumentSummary[]>(`${this.api}/documents`)
      .subscribe(x => this.documents = x);
  }

  upload(event: Event) {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    const form = new FormData();
    form.append('file', file);

    this.loading = true;
    this.http.post<DocumentSummary>(`${this.api}/documents/upload`, form)
      .subscribe({
        next: doc => {
          this.documents = [doc, ...this.documents];
          this.selected = doc;
          this.loading = false;
        },
        error: () => this.loading = false
      });
  }

  select(doc: DocumentSummary) {
    this.selected = doc;
    this.messages = [];
  }

  ask() {
    if (!this.selected || !this.question.trim()) return;

    const question = this.question.trim();
    this.messages.push({ role: 'user', text: question });
    this.question = '';
    this.loading = true;

    this.http.post<ChatResult>(`${this.api}/chat`, {
      documentId: this.selected.id,
      question
    }).subscribe({
      next: result => {
        this.messages.push({
          role: 'assistant',
          text: result.answer,
          sources: result.sources
        });
        this.loading = false;
      },
      error: () => {
        this.messages.push({
          role: 'assistant',
          text: 'Error while processing the question.'
        });
        this.loading = false;
      }
    });
  }
}
